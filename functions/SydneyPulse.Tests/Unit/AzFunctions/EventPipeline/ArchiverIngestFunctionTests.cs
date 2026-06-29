// ArchiverIngestFunctionTests.cs
// ------------------------------
// Unit tests for ArchiverIngestFunction (SP1-15, ADR-0012).
// Tests pure helpers via InternalsVisibleTo on SydneyPulse.Functions.

using System.Text;
using Azure;
using Azure.Messaging;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Extensions.Logging;
using Moq;
using SydneyPulse.Core.Archive;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions;
using SydneyPulse.Functions.Archive;
using SydneyPulse.Functions.AzFunctions.EventPipeline;
using Xunit;

namespace SydneyPulse.Tests.Unit.AzFunctions.EventPipeline;

public class ArchiverIngestFunctionTests
{
    // Mocks for the AppendToPendingAsync tests: IPendingBlobStore wraps the SDK
    // hop (BlobServiceClient → BlobContainerClient → GetAppendBlobClient is an
    // extension method that Moq cannot mock). Constructed once per test —
    // xUnit creates a fresh instance per [Fact] so state never leaks.
    private readonly Mock<IPendingBlobStore> _mockPendingStore;
    private readonly Mock<AppendBlobClient> _mockAppendBlob;
    private readonly ArchiverIngestFunction _function;

    public ArchiverIngestFunctionTests()
    {
        _mockPendingStore = new Mock<IPendingBlobStore>();
        _mockAppendBlob = new Mock<AppendBlobClient>();

        // Any partition path resolves to the same mock append blob — tests
        // assert on the path argument via Verify rather than constraining Setup.
        _mockPendingStore
            .Setup(s => s.GetAppendBlob(It.IsAny<string>()))
            .Returns(_mockAppendBlob.Object);

        // Idempotent create — returns a non-null Response so awaiters don't NRE.
        // Matcher types use the SDK's non-nullable declarations (no `?`) so the
        // expression-tree fingerprint matches calls that omit optional args.
        _mockAppendBlob
            .Setup(c => c.CreateIfNotExistsAsync(
                It.IsAny<AppendBlobCreateOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Default AppendBlockAsync setups — Azure SDK exposes TWO overloads:
        //   3-arg modern: (Stream, AppendBlobAppendBlockOptions, CT)
        //   5-arg legacy: (Stream, byte[], AppendBlobRequestConditions, IProgress<long>, CT)
        // Production code may call either; mocking both lets the simpler call shape
        // `AppendBlockAsync(stream, cancellationToken: ct)` work regardless of which
        // the compiler picks. Individual tests may override with a Callback.
        _mockAppendBlob
            .Setup(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<AppendBlobAppendBlockOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobAppendInfo>>());
        _mockAppendBlob
            .Setup(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<byte[]>(),
                It.IsAny<AppendBlobRequestConditions>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobAppendInfo>>());

        var logger = Mock.Of<ILogger<ArchiverIngestFunction>>();
        _function = new ArchiverIngestFunction(_mockPendingStore.Object, logger);
    }

    #region Helpers

    // Helper: VehicleUpdate with sensible defaults; overridable for test variations.
    private static VehicleUpdate MakeVehicleUpdate(
        string vehicleId = "VH-001",
        DateTimeOffset? vehicleTimestamp = null) => new(
            VehicleId: vehicleId,
            TripId: "TRIP-1",
            RouteId: "NTH_1a",
            RouteShortName: "T1",
            RouteLongName: "T1 North Shore Line",
            RouteColor: "#F99D1C",
            Mode: "sydneytrains",
            Latitude: -33.8688,
            Longitude: 151.2093,
            Bearing: 90f,
            SpeedKmh: 60f,
            OccupancyStatus: "MANY_SEATS_AVAILABLE",
            VehicleTimestamp: vehicleTimestamp ?? new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero));

    // Helper: ServiceAlert with sensible defaults; overridable for test variations.
    private static ServiceAlert MakeServiceAlert(
        string alertId = "ALERT-1",
        DateTimeOffset? startsAt = null) => new(
            AlertId: alertId,
            RouteShortName: "T1",
            Severity: "significant_delays",
            HeaderText: "Delays on T1",
            DescriptionText: "Significant delays expected",
            StartsAt: startsAt,
            EndsAt: null);

    // Helper: construct CloudEvent with JSON-serialised payload (matches the shape
    // Event Grid delivers in production).
    private static CloudEvent MakeCloudEvent(
        string type,
        object data,
        string id = "ce-test-1",
        DateTimeOffset? time = null) => new(
            source: "/sydney-pulse/poller",
            type: type,
            jsonSerializableData: data)
        {
            Id = id,
            Time = time
        };

    // Helper: ArchiveEvent shaped like a VehicleUpdate row, with overridable timestamps.
    // Used by AppendToPendingAsync tests where the contents are only loosely asserted
    // and the focus is on partition path + call shape.
    private static ArchiveEvent MakeArchiveEvent(
        DateTimeOffset? sourceTimestamp = null,
        DateTimeOffset? archivedAt = null,
        string eventId = "evt-1",
        string routeShortName = "T1")
    {
        var src = sourceTimestamp ?? new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero);
        var arc = archivedAt ?? new DateTimeOffset(2026, 6, 4, 14, 30, 5, TimeSpan.Zero);
        return new ArchiveEvent(
            EventId: eventId,
            EventType: "com.sydneypulse.VehicleUpdate.v1",
            EventVersion: "v1",
            SourceTimestamp: src,
            PublishedAt: src,
            ArchivedAt: arc,
            RouteShortName: routeShortName,
            VehicleId: "VH-001",
            TripId: "TRIP-1",
            RouteId: "NTH_1a",
            RouteLongName: "T1 North Shore Line",
            RouteColor: "#F99D1C",
            Mode: "sydneytrains",
            Latitude: -33.8688,
            Longitude: 151.2093,
            Bearing: 90f,
            SpeedKmh: 60f,
            OccupancyStatus: "MANY_SEATS_AVAILABLE",
            AlertId: null,
            Severity: null,
            HeaderText: null,
            DescriptionText: null,
            StartsAt: null,
            EndsAt: null);
    }

    #endregion

    #region MapToArchiveEvent — VehicleUpdate

    // Happy path: VehicleUpdate.v1 → all vehicle fields populated, all alert fields null,
    // three timestamps derived correctly, EventVersion extracted from type suffix.
    [Fact]
    public void MapToArchiveEvent_VehicleUpdate_PopulatesVehicleFieldsAndLeavesAlertFieldsNull()
    {
        // Arrange
        var vehicleTimestamp = new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero);
        var vehicleUpdate = MakeVehicleUpdate(vehicleTimestamp: vehicleTimestamp);
        var eventTime = new DateTimeOffset(2026, 6, 4, 14, 30, 1, TimeSpan.Zero);
        var archivedAt = new DateTimeOffset(2026, 6, 4, 14, 30, 5, TimeSpan.Zero);
        var ce = MakeCloudEvent("com.sydneypulse.VehicleUpdate.v1", vehicleUpdate,
            id: "ce-001", time: eventTime);

        // Act
        var result = ArchiverIngestFunction.MapToArchiveEvent(ce, archivedAt);

        // Assert — identity & discrimination
        Assert.Equal("ce-001", result.EventId);
        Assert.Equal("com.sydneypulse.VehicleUpdate.v1", result.EventType);
        Assert.Equal("v1", result.EventVersion);

        // Three timestamps
        Assert.Equal(vehicleTimestamp, result.SourceTimestamp);
        Assert.Equal(eventTime, result.PublishedAt);
        Assert.Equal(archivedAt, result.ArchivedAt);

        // Common routing key
        Assert.Equal("T1", result.RouteShortName);

        // Vehicle-specific fields populated
        Assert.Equal("VH-001", result.VehicleId);
        Assert.Equal("TRIP-1", result.TripId);
        Assert.Equal("NTH_1a", result.RouteId);
        Assert.Equal("T1 North Shore Line", result.RouteLongName);
        Assert.Equal("#F99D1C", result.RouteColor);
        Assert.Equal("sydneytrains", result.Mode);
        Assert.Equal(-33.8688, result.Latitude);
        Assert.Equal(151.2093, result.Longitude);
        Assert.Equal(90f, result.Bearing);
        Assert.Equal(60f, result.SpeedKmh);
        Assert.Equal("MANY_SEATS_AVAILABLE", result.OccupancyStatus);

        // Alert-specific fields null
        Assert.Null(result.AlertId);
        Assert.Null(result.Severity);
        Assert.Null(result.HeaderText);
        Assert.Null(result.DescriptionText);
        Assert.Null(result.StartsAt);
        Assert.Null(result.EndsAt);
    }

    #endregion

    #region MapToArchiveEvent — ServiceAlert

    // Happy path: ServiceAlert.v1 → all alert fields populated, all vehicle fields null,
    // SourceTimestamp = StartsAt (since StartsAt is present).
    [Fact]
    public void MapToArchiveEvent_ServiceAlert_PopulatesAlertFieldsAndLeavesVehicleFieldsNull()
    {
        // Arrange
        var startsAt = new DateTimeOffset(2026, 6, 4, 15, 0, 0, TimeSpan.Zero);
        var alert = MakeServiceAlert(alertId: "ALERT-001", startsAt: startsAt);
        var eventTime = new DateTimeOffset(2026, 6, 4, 14, 30, 1, TimeSpan.Zero);
        var archivedAt = new DateTimeOffset(2026, 6, 4, 14, 30, 5, TimeSpan.Zero);
        var ce = MakeCloudEvent("com.sydneypulse.ServiceAlert.v1", alert,
            id: "ce-alert-001", time: eventTime);

        // Act
        var result = ArchiverIngestFunction.MapToArchiveEvent(ce, archivedAt);

        // Assert — identity & discrimination
        Assert.Equal("ce-alert-001", result.EventId);
        Assert.Equal("com.sydneypulse.ServiceAlert.v1", result.EventType);
        Assert.Equal("v1", result.EventVersion);

        // Three timestamps — SourceTimestamp comes from StartsAt (alert's source moment)
        Assert.Equal(startsAt, result.SourceTimestamp);
        Assert.Equal(eventTime, result.PublishedAt);
        Assert.Equal(archivedAt, result.ArchivedAt);

        // Common routing key
        Assert.Equal("T1", result.RouteShortName);

        // Alert-specific fields populated
        Assert.Equal("ALERT-001", result.AlertId);
        Assert.Equal("significant_delays", result.Severity);
        Assert.Equal("Delays on T1", result.HeaderText);
        Assert.Equal("Significant delays expected", result.DescriptionText);
        Assert.Equal(startsAt, result.StartsAt);
        Assert.Null(result.EndsAt);

        // Vehicle-specific fields null
        Assert.Null(result.VehicleId);
        Assert.Null(result.TripId);
        Assert.Null(result.RouteId);
        Assert.Null(result.RouteLongName);
        Assert.Null(result.RouteColor);
        Assert.Null(result.Mode);
        Assert.Null(result.Latitude);
        Assert.Null(result.Longitude);
        Assert.Null(result.Bearing);
        Assert.Null(result.SpeedKmh);
        Assert.Null(result.OccupancyStatus);
    }

    #endregion

    #region MapToArchiveEvent — SourceTimestamp fallback

    // Contract: for alerts, SourceTimestamp resolves via StartsAt ?? cloudEvent.Time ?? archivedAt.
    // This test pins the middle fallback: StartsAt null → use cloudEvent.Time.
    [Fact]
    public void MapToArchiveEvent_AlertWithNullStartsAt_FallsBackToCloudEventTime()
    {
        // Arrange — alert with NO StartsAt; CloudEvent has Time set
        var alert = MakeServiceAlert(startsAt: null);
        var eventTime = new DateTimeOffset(2026, 6, 4, 14, 30, 1, TimeSpan.Zero);
        var archivedAt = new DateTimeOffset(2026, 6, 4, 14, 30, 5, TimeSpan.Zero);
        var ce = MakeCloudEvent("com.sydneypulse.ServiceAlert.v1", alert, time: eventTime);

        // Act
        var result = ArchiverIngestFunction.MapToArchiveEvent(ce, archivedAt);

        // Assert — SourceTimestamp = cloudEvent.Time (NOT archivedAt; that's the deeper fallback)
        Assert.Equal(eventTime, result.SourceTimestamp);
        // StartsAt itself is preserved as null in the archive row
        Assert.Null(result.StartsAt);
    }

    // Contract: deepest fallback — StartsAt null AND cloudEvent.Time null → archivedAt.
    // Pins the third arm of the null-coalescing chain so a future edit can't silently
    // swap `archivedAt` for `DateTimeOffset.UtcNow` without turning red.
    [Fact]
    public void MapToArchiveEvent_AlertWithNullStartsAtAndNullTime_FallsBackToArchivedAt()
    {
        // Arrange — alert with NO StartsAt; CloudEvent with NO Time
        var alert = MakeServiceAlert(startsAt: null);
        var archivedAt = new DateTimeOffset(2026, 6, 4, 14, 30, 5, TimeSpan.Zero);
        var ce = MakeCloudEvent("com.sydneypulse.ServiceAlert.v1", alert, time: null);

        // Act
        var result = ArchiverIngestFunction.MapToArchiveEvent(ce, archivedAt);

        // Assert — both timestamp fallbacks land on archivedAt
        Assert.Equal(archivedAt, result.SourceTimestamp);
        Assert.Equal(archivedAt, result.PublishedAt);
        // StartsAt itself preserved as null
        Assert.Null(result.StartsAt);
    }

    #endregion

    #region MapToArchiveEvent — unknown event type

    // Defensive contract: an unknown event type means schema drift or malicious input.
    // Throw loudly so Event Grid retries and eventually dead-letters — better than
    // silently archiving a misshaped row that breaks downstream analytics queries.
    [Fact]
    public void MapToArchiveEvent_UnknownEventType_Throws()
    {
        // Arrange — unknown event type
        var ce = MakeCloudEvent("com.sydneypulse.MysteryEvent.v1", new { Foo = "bar" });
        var archivedAt = DateTimeOffset.UtcNow;

        // Act + Assert
        Assert.Throws<InvalidOperationException>(() =>
            ArchiverIngestFunction.MapToArchiveEvent(ce, archivedAt));
    }

    #endregion

    #region AppendToPendingAsync

    // Happy path contract: an event lands in the right container, at the right
    // Hive partition path, via CreateIfNotExists + AppendBlock, with JSONL +
    // newline as the payload.
    [Fact]
    public async Task AppendToPendingAsync_VehicleUpdate_AppendsJsonlToHivePartitionPath()
    {
        // Arrange — event at hour 14; expected partition path uses that hour
        var sourceTs = new DateTimeOffset(2026, 6, 4, 14, 30, 0, TimeSpan.Zero);
        var archiveEvent = MakeArchiveEvent(sourceTimestamp: sourceTs);

        // Capture the bytes the implementation pushes to AppendBlockAsync so we
        // can assert on the JSONL line shape. Set the Callback on BOTH overloads so
        // the test is robust to whichever overload the implementation calls (3-arg
        // modern or 5-arg legacy — see the constructor setup for the rationale).
        byte[]? capturedBytes = null;
        void Capture(Stream stream)
        {
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            capturedBytes = ms.ToArray();
        }
        _mockAppendBlob
            .Setup(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<AppendBlobAppendBlockOptions>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, AppendBlobAppendBlockOptions, CancellationToken>(
                (stream, _, _) => Capture(stream))
            .ReturnsAsync(Mock.Of<Response<BlobAppendInfo>>());
        _mockAppendBlob
            .Setup(c => c.AppendBlockAsync(
                It.IsAny<Stream>(),
                It.IsAny<byte[]>(),
                It.IsAny<AppendBlobRequestConditions>(),
                It.IsAny<IProgress<long>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, byte[], AppendBlobRequestConditions, IProgress<long>, CancellationToken>(
                (stream, _, _, _, _) => Capture(stream))
            .ReturnsAsync(Mock.Of<Response<BlobAppendInfo>>());

        // Act
        await _function.AppendToPendingAsync(archiveEvent, CancellationToken.None);

        // Assert — store resolves the blob at the Hive partition path (from
        // SourceTimestamp) + events.jsonl. Container choice is the store's job.
        var expectedPath = $"yyyy=2026/MM=06/dd=04/HH=14/{FunctionConstants.PendingEventsBlobName}";
        _mockPendingStore.Verify(s =>
            s.GetAppendBlob(expectedPath), Times.Once);

        // Assert — first-write idempotent create
        _mockAppendBlob.Verify(c => c.CreateIfNotExistsAsync(
            It.IsAny<AppendBlobCreateOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        // Assert — exactly one block appended (and via this, that the JSONL payload
        // is well-formed). Non-null capturedBytes proves AppendBlockAsync was called
        // — works whichever SDK overload the implementation picked.
        Assert.NotNull(capturedBytes);
        var jsonl = Encoding.UTF8.GetString(capturedBytes!);
        Assert.Contains(archiveEvent.EventId, jsonl);
        Assert.EndsWith("\n", jsonl);
    }

    // Late-event contract: partition path follows SourceTimestamp (when the event
    // happened), NOT ArchivedAt (when we ingested it). This is what makes the
    // partition layout time-correct under at-least-once delivery + retries.
    [Fact]
    public async Task AppendToPendingAsync_LateEvent_PartitionPathFollowsSourceTimestampNotArchivedAt()
    {
        // Arrange — event happened in hour 13 but we're ingesting it in hour 14
        var sourceTs = new DateTimeOffset(2026, 6, 4, 13, 55, 0, TimeSpan.Zero);
        var archivedAt = new DateTimeOffset(2026, 6, 4, 14, 5, 0, TimeSpan.Zero);
        var archiveEvent = MakeArchiveEvent(sourceTimestamp: sourceTs, archivedAt: archivedAt);

        // Act
        await _function.AppendToPendingAsync(archiveEvent, CancellationToken.None);

        // Assert — blob path lands in hour 13's partition, not hour 14's
        var expectedPath = $"yyyy=2026/MM=06/dd=04/HH=13/{FunctionConstants.PendingEventsBlobName}";
        _mockPendingStore.Verify(s =>
            s.GetAppendBlob(expectedPath), Times.Once);
    }

    #endregion

    #region RunAsync (orchestration)

    // End-to-end orchestration: a CloudEvent arrives at the trigger, and the
    // resulting append lands at a path derived from the event's SourceTimestamp.
    // Pins the wiring: RunAsync → MapToArchiveEvent → AppendToPendingAsync.
    // (Individual contracts of Map and Append are covered in their own regions.)
    [Fact]
    public async Task RunAsync_VehicleUpdate_AppendsAtPartitionDerivedFromVehicleTimestamp()
    {
        // Arrange — VU at 11:30 UTC; expected partition is hour 11
        var vehicleTs = new DateTimeOffset(2026, 6, 5, 11, 30, 0, TimeSpan.Zero);
        var vehicle = MakeVehicleUpdate(vehicleTimestamp: vehicleTs);
        var ce = MakeCloudEvent("com.sydneypulse.VehicleUpdate.v1", vehicle);

        // Act
        await _function.RunAsync(ce, CancellationToken.None);

        // Assert — store resolved the blob at the SourceTimestamp-derived path
        var expectedPath = $"yyyy=2026/MM=06/dd=05/HH=11/{FunctionConstants.PendingEventsBlobName}";
        _mockPendingStore.Verify(s => s.GetAppendBlob(expectedPath), Times.Once);
    }

    #endregion
}
