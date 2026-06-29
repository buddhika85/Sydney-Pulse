// TfNswFeedClientTests.cs
// -----------------------
// Unit tests for TfNswFeedClient.
// HTTP is stubbed via FuncHttpMessageHandler — no real network calls.

using System.IO.Compression;
using System.Text;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SydneyPulse.Core.TfNsw;
using TransitRealtime;
using Xunit;

namespace SydneyPulse.Tests.Unit;

public class TfNswFeedClientTests
{
    // ── Test helpers ──────────────────────────────────────────────────────────

    // Minimal HttpMessageHandler stub — returns whatever the delegate produces.
    private sealed class FuncHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }

    private static TfNswFeedClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("TfNsw")).Returns(httpClient);
        var options = Options.Create(new TfNswOptions
        {
            ApiKey = "test-key",
            BaseUrl = "https://api.transport.nsw.gov.au"
        });
        return new TfNswFeedClient(factory.Object, options, NullLogger<TfNswFeedClient>.Instance);
    }

    // Serialises a one-vehicle GTFS-RT feed to protobuf bytes.
    private static byte[] BuildVehicleFeed(
        string vehicleId, string routeId, float lat, float lon, ulong timestamp)
    {
        var feed = new FeedMessage
        {
            Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = timestamp },
            Entity =
            {
                new FeedEntity
                {
                    Id = vehicleId,
                    Vehicle = new VehiclePosition
                    {
                        Vehicle   = new VehicleDescriptor { Id = vehicleId },
                        Trip      = new TripDescriptor { RouteId = routeId, TripId = "trip1" },
                        Position  = new Position { Latitude = lat, Longitude = lon, Speed = 12f },
                        Timestamp = timestamp,
                        OccupancyStatus = VehiclePosition.Types.OccupancyStatus.FewSeatsAvailable
                    }
                }
            }
        };
        return feed.ToByteArray();
    }

    // Creates an in-memory ZIP containing a routes.txt with the given CSV content.
    private static byte[] BuildRoutesZip(string csvContent)
    {
        using var ms = new MemoryStream();
        // leaveOpen: true so ms stays readable after the ZipArchive is disposed.
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = zip.CreateEntry("routes.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(csvContent);
        }
        return ms.ToArray();
    }

    // ── Tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetVehiclePositionsAsync_WithValidFeed_ReturnsMappedPosition()
    {
        // Arrange
        const ulong ts = 1_748_000_000UL;
        var feedBytes = BuildVehicleFeed("v001", "NTH_1a", -33.8f, 151.2f, ts);
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore Line,F99D1C\n");

        var handler = new FuncHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/vehiclepos/")
                ? new HttpResponseMessage { Content = new ByteArrayContent(feedBytes) }
                : new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) });

        var client = CreateClient(handler);

        // Act
        var positions = await client.GetVehiclePositionsAsync("sydneytrains");

        // Assert
        Assert.Single(positions);
        var pos = positions[0];
        Assert.Equal("v001", pos.VehicleId);
        Assert.Equal("NTH_1a", pos.RouteId);
        Assert.Equal("T1", pos.RouteShortName);
        Assert.Equal("#F99D1C", pos.RouteColor);
        Assert.Equal(-33.8f, (float)pos.Latitude, precision: 1);
        Assert.Equal(151.2f, (float)pos.Longitude, precision: 1);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds((long)ts), pos.VehicleTimestamp);
    }

    [Fact]
    public async Task GetRoutesAsync_SecondCallSameMode_DoesNotFetchAgain()
    {
        // Arrange: count how many HTTP calls the handler receives.
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore Line,F99D1C\n");
        int callCount = 0;

        var handler = new FuncHttpMessageHandler(_ =>
        {
            callCount++;
            return new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) };
        });

        var client = CreateClient(handler);

        // Act: two calls for the same mode
        await client.GetRoutesAsync("sydneytrains");
        await client.GetRoutesAsync("sydneytrains");

        // Assert: second call must hit the in-memory cache, not the network.
        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task GetRoutesAsync_ParsesShortNameAndNormalisesColor()
    {
        // Arrange: GTFS stores colour without '#'; the client must add it.
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore & Western Line,F99D1C\n");

        var handler = new FuncHttpMessageHandler(_ =>
            new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) });

        var client = CreateClient(handler);

        // Act
        var routes = await client.GetRoutesAsync("sydneytrains");

        // Assert
        Assert.True(routes.ContainsKey("NTH_1a"));
        var route = routes["NTH_1a"];
        Assert.Equal("T1", route.ShortName);
        Assert.Equal("North Shore & Western Line", route.LongName);
        Assert.Equal("#F99D1C", route.Color);
        Assert.Equal("sydneytrains", route.Mode);
    }

    // ── GetServiceAlertsAsync — RouteShortName lookup (SP1-16 follow-up) ──────
    // These three tests pin the contract found during the 2026-06-16 DLQ
    // analysis: alerts must enrich `route_id` via the static-routes cache
    // (mirrors GetVehiclePositionsAsync), not write the raw route_id into the
    // RouteShortName field. Before the fix this method skipped the lookup and
    // every Cosmos `alerts` partition key would have been an internal route_id
    // (`SCO_2a`, `WST_1a`) instead of a user-facing label (`T1`).

    // Serialises a one-alert GTFS-RT feed to protobuf bytes.
    // Pass `informedRouteIds` empty for the "no informed entity" case.
    private static byte[] BuildAlertFeed(
        string alertId, string[] informedRouteIds, string headerText)
    {
        var alert = new Alert
        {
            Effect = Alert.Types.Effect.ModifiedService,
            HeaderText = new TranslatedString
            {
                Translation =
                {
                    new TranslatedString.Types.Translation { Text = headerText, Language = "en" }
                }
            }
        };

        // InformedEntity carries the route_id(s) the alert applies to.
        foreach (var routeId in informedRouteIds)
        {
            alert.InformedEntity.Add(new EntitySelector { RouteId = routeId });
        }

        var feed = new FeedMessage
        {
            Header = new FeedHeader { GtfsRealtimeVersion = "2.0", Timestamp = 1_748_000_000UL },
            Entity = { new FeedEntity { Id = alertId, Alert = alert } }
        };
        return feed.ToByteArray();
    }

    [Fact]
    public async Task GetServiceAlertsAsync_WithRouteInCache_LooksUpShortName()
    {
        // Arrange — alert references NTH_1a, routes.txt maps NTH_1a → T1.
        var alertBytes = BuildAlertFeed("alert-1", ["NTH_1a"], "Some delays on T1");
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore Line,F99D1C\n");

        // Handler routes `/alerts/` to the alerts feed, everything else (the
        // GTFS static ZIP fetch) to the routes payload.
        var handler = new FuncHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/alerts/")
                ? new HttpResponseMessage { Content = new ByteArrayContent(alertBytes) }
                : new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) });

        var client = CreateClient(handler);

        // Act
        var alerts = await client.GetServiceAlertsAsync("sydneytrains");

        // Assert — the looked-up user-facing label, not the internal route_id.
        Assert.Single(alerts);
        Assert.Equal("T1", alerts[0].RouteShortName);
    }

    [Fact]
    public async Task GetServiceAlertsAsync_WithRouteNotInCache_FallsBackToRouteId()
    {
        // Arrange — alert references "4T.T.SCO" (a service-specific code observed
        // in real DLQ data) that isn't present in routes.txt. The mapper should
        // fall back to the raw route_id rather than emit an empty short name.
        var alertBytes = BuildAlertFeed("alert-2", ["4T.T.SCO"], "Buses replace trains");
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore Line,F99D1C\n");

        var handler = new FuncHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/alerts/")
                ? new HttpResponseMessage { Content = new ByteArrayContent(alertBytes) }
                : new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) });

        var client = CreateClient(handler);

        // Act
        var alerts = await client.GetServiceAlertsAsync("sydneytrains");

        // Assert — fallback preserves the raw route_id (graceful degradation,
        // same pattern as GetVehiclePositionsAsync).
        Assert.Single(alerts);
        Assert.Equal("4T.T.SCO", alerts[0].RouteShortName);
    }

    [Fact]
    public async Task GetServiceAlertsAsync_WithNoInformedEntity_RouteShortNameIsEmpty()
    {
        // Arrange — alert with zero InformedEntity entries (general notices).
        // Mapper must not throw; should emit empty RouteShortName.
        var alertBytes = BuildAlertFeed("alert-3", [], "Network-wide notice");
        var zipBytes = BuildRoutesZip(
            "route_id,route_short_name,route_long_name,route_color\n" +
            "NTH_1a,T1,North Shore Line,F99D1C\n");

        var handler = new FuncHttpMessageHandler(req =>
            req.RequestUri!.AbsolutePath.Contains("/alerts/")
                ? new HttpResponseMessage { Content = new ByteArrayContent(alertBytes) }
                : new HttpResponseMessage { Content = new ByteArrayContent(zipBytes) });

        var client = CreateClient(handler);

        // Act
        var alerts = await client.GetServiceAlertsAsync("sydneytrains");

        // Assert — no route_id available; empty string is the documented contract.
        Assert.Single(alerts);
        Assert.Equal(string.Empty, alerts[0].RouteShortName);
    }
}
