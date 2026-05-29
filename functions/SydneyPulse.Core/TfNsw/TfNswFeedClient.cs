// TfNswFeedClient.cs
// ------------------
// Fetches GTFS-Realtime vehicle positions and service alerts from the TfNSW Open Data API.
// Enriches realtime data with static route metadata (short name, colour) from a 1-hour
// in-memory cache per transport mode (ADR-0009).
// Registered as singleton in Program.cs so the cache is shared across Function invocations.

using System.IO.Compression;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.Events;
using TransitRealtime;

namespace SydneyPulse.Core.TfNsw;

public class TfNswFeedClient : ITfNswFeedClient
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly TfNswOptions _options;
    private readonly ILogger<TfNswFeedClient> _logger;

    // Per-mode route cache keyed by transport mode string (ADR-0009).
    private readonly Dictionary<string, IReadOnlyDictionary<string, RouteInfo>> _routeCache = new();
    private readonly Dictionary<string, DateTimeOffset> _cacheExpiry = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    // Prevents concurrent duplicate cache refreshes for the same mode.
    private readonly SemaphoreSlim _cacheLock = new(1, 1);

    public TfNswFeedClient(
        IHttpClientFactory httpClientFactory,
        IOptions<TfNswOptions> options,
        ILogger<TfNswFeedClient> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyList<VehicleUpdate>> GetVehiclePositionsAsync(
        string mode, CancellationToken cancellationToken = default)
    {
        var bytes = await FetchBytesAsync($"/v2/gtfs/vehiclepos/{mode}", cancellationToken);
        var feed = FeedMessage.Parser.ParseFrom(bytes);
        var routes = await GetRoutesAsync(mode, cancellationToken);

        var results = new List<VehicleUpdate>(feed.Entity.Count);
        foreach (var entity in feed.Entity)
        {
            if (entity.Vehicle is not { } v) continue;

            var routeId = v.Trip?.RouteId ?? string.Empty;
            routes.TryGetValue(routeId, out var route);

            results.Add(new VehicleUpdate(
                VehicleId: v.Vehicle?.Id ?? entity.Id,
                TripId: v.Trip?.TripId ?? string.Empty,
                RouteId: routeId,
                RouteShortName: route?.ShortName ?? routeId,
                RouteLongName: route?.LongName ?? string.Empty,
                RouteColor: route?.Color ?? string.Empty,
                Mode: mode,
                Latitude: v.Position?.Latitude ?? 0,
                Longitude: v.Position?.Longitude ?? 0,
                Bearing: v.Position?.Bearing,
                SpeedKmh: v.Position?.Speed,
                OccupancyStatus: v.OccupancyStatus.ToString(),
                VehicleTimestamp: v.Timestamp > 0
                    ? DateTimeOffset.FromUnixTimeSeconds((long)v.Timestamp)
                    : DateTimeOffset.UtcNow));
        }

        _logger.LogInformation("Fetched {Count} vehicle positions for mode {Mode}", results.Count, mode);
        return results;
    }

    public async Task<IReadOnlyList<ServiceAlert>> GetServiceAlertsAsync(
        string mode, CancellationToken cancellationToken = default)
    {
        var bytes = await FetchBytesAsync($"/v2/gtfs/alerts/{mode}", cancellationToken);
        var feed = FeedMessage.Parser.ParseFrom(bytes);

        var results = new List<ServiceAlert>(feed.Entity.Count);
        foreach (var entity in feed.Entity)
        {
            if (entity.Alert is not { } alert) continue;

            var routeId = alert.InformedEntity.FirstOrDefault()?.RouteId ?? string.Empty;
            var header = alert.HeaderText?.Translation.FirstOrDefault()?.Text ?? string.Empty;
            var description = alert.DescriptionText?.Translation.FirstOrDefault()?.Text ?? string.Empty;
            var period = alert.ActivePeriod.FirstOrDefault();

            results.Add(new ServiceAlert(
                AlertId: entity.Id,
                RouteShortName: routeId,
                Severity: alert.Effect.ToString().ToLowerInvariant(),
                HeaderText: header,
                DescriptionText: description,
                StartsAt: period?.Start > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)period.Start) : null,
                EndsAt: period?.End > 0 ? DateTimeOffset.FromUnixTimeSeconds((long)period.End) : null));
        }

        return results;
    }

    public async Task<IReadOnlyDictionary<string, RouteInfo>> GetRoutesAsync(
        string mode, CancellationToken cancellationToken = default)
    {
        // Fast path — no lock needed for a warm cache hit.
        if (_cacheExpiry.TryGetValue(mode, out var expiry) && expiry > DateTimeOffset.UtcNow)
            return _routeCache[mode];

        await _cacheLock.WaitAsync(cancellationToken);
        try
        {
            // Re-check after acquiring the lock to prevent duplicate fetches.
            if (_cacheExpiry.TryGetValue(mode, out expiry) && expiry > DateTimeOffset.UtcNow)
                return _routeCache[mode];

            var routes = await FetchAndParseStaticRoutesAsync(mode, cancellationToken);
            _routeCache[mode] = routes;
            _cacheExpiry[mode] = DateTimeOffset.UtcNow.Add(CacheTtl);
            _logger.LogInformation("Refreshed GTFS static cache for mode {Mode} ({Count} routes)", mode, routes.Count);
            return routes;
        }
        finally
        {
            _cacheLock.Release();
        }
    }

    // Downloads the GTFS static ZIP and extracts route metadata from routes.txt.
    private async Task<IReadOnlyDictionary<string, RouteInfo>> FetchAndParseStaticRoutesAsync(
        string mode, CancellationToken cancellationToken)
    {
        var zipBytes = await FetchBytesAsync($"/v1/gtfs/schedule/{mode}", cancellationToken);
        using var zip = new ZipArchive(new MemoryStream(zipBytes), ZipArchiveMode.Read);

        var entry = zip.GetEntry("routes.txt");
        if (entry is null)
        {
            _logger.LogWarning("No routes.txt found in GTFS static ZIP for mode {Mode}", mode);
            return new Dictionary<string, RouteInfo>();
        }

        using var reader = new StreamReader(entry.Open(), Encoding.UTF8);
        return ParseRoutesCsv(reader, mode);
    }

    // Parses GTFS routes.txt into a RouteId → RouteInfo dictionary.
    private static IReadOnlyDictionary<string, RouteInfo> ParseRoutesCsv(TextReader reader, string mode)
    {
        var headerLine = reader.ReadLine();
        if (headerLine is null) return new Dictionary<string, RouteInfo>();

        var headers = SplitCsvLine(headerLine);
        int iId    = Array.IndexOf(headers, "route_id");
        int iShort = Array.IndexOf(headers, "route_short_name");
        int iLong  = Array.IndexOf(headers, "route_long_name");
        int iColor = Array.IndexOf(headers, "route_color");

        var dict = new Dictionary<string, RouteInfo>(StringComparer.OrdinalIgnoreCase);
        string? line;
        while ((line = reader.ReadLine()) is not null)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var fields = SplitCsvLine(line);
            var id = GetField(fields, iId);
            if (string.IsNullOrEmpty(id)) continue;

            var rawColor = GetField(fields, iColor);
            dict[id] = new RouteInfo(
                RouteId: id,
                ShortName: GetField(fields, iShort),
                LongName: GetField(fields, iLong),
                // GTFS stores colour without '#'; normalise to always include it.
                Color: rawColor.Length > 0 ? "#" + rawColor.TrimStart('#') : string.Empty,
                Mode: mode);
        }

        return dict;
    }

    // Splits a single CSV line, respecting double-quoted fields that may contain commas.
    private static string[] SplitCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        foreach (var ch in line)
        {
            if (ch == '"')            { inQuotes = !inQuotes; }
            else if (ch == ',' && !inQuotes) { fields.Add(current.ToString()); current.Clear(); }
            else                      { current.Append(ch); }
        }
        fields.Add(current.ToString());
        return [.. fields];
    }

    private static string GetField(string[] fields, int index) =>
        index >= 0 && index < fields.Length ? fields[index].Trim() : string.Empty;

    // Sends an authenticated GET to the TfNSW API and returns the raw response bytes.
    private async Task<byte[]> FetchBytesAsync(string path, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("TfNsw");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{_options.BaseUrl}{path}");
        // API key auth per TfNSW Open Data portal requirements.
        request.Headers.Add("Authorization", $"apikey {_options.ApiKey}");
        var response = await client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }
}
