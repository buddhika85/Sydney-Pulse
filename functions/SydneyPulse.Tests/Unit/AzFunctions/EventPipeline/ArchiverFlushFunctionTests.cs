// ArchiverFlushFunctionTests.cs
// -----------------------------
// Unit tests for ArchiverFlushFunction (SP1-15, ADR-0012).
// Drives the internal helper methods directly via InternalsVisibleTo —
// I/O is mocked at the IPendingBlobStore + AppendBlobClient seam.

using System.Text;
using System.Text.Json;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using SydneyPulse.Core.Archive;
using SydneyPulse.Functions;
using SydneyPulse.Functions.Archive;
using SydneyPulse.Functions.AzFunctions.EventPipeline;
using Xunit;

namespace SydneyPulse.Tests.Unit.AzFunctions.EventPipeline;

public class ArchiverFlushFunctionTests
{
    // Mock chains:
    //   Pending side:  IPendingBlobStore → AppendBlobClient
    //                  (DownloadContentAsync configured per ReadPendingEvents test)
    //   Archive side:  BlobServiceClient → BlobContainerClient → BlobClient
    //                  (UploadAsync configured for WriteManifest happy-path test)
    private readonly Mock<IPendingBlobStore> _mockPendingStore;
    private readonly Mock<AppendBlobClient> _mockAppendBlob;
    private readonly Mock<BlobServiceClient> _mockBlobService;
    private readonly Mock<BlobContainerClient> _mockArchiveContainer;
    private readonly Mock<BlobClient> _mockArchiveBlob;
    private readonly Mock<IParquetArchiveWriter> _mockParquetWriter;
    private readonly ArchiverFlushFunction _function;

    public ArchiverFlushFunctionTests()
    {
        _mockPendingStore = new Mock<IPendingBlobStore>();
        _mockAppendBlob = new Mock<AppendBlobClient>();

        // Any partition path resolves to the same mock blob — tests assert
        // on the path argument via Verify rather than constraining Setup.
        _mockPendingStore
            .Setup(s => s.GetAppendBlob(It.IsAny<string>()))
            .Returns(_mockAppendBlob.Object);

        // Default: ListPartitionPathsAsync returns an empty stream. RunAsync
        // tests override per-scenario.
        _mockPendingStore
            .Setup(s => s.ListPartitionPathsAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable());

        // Default DeleteAsync stub on the pending blob — Flush calls this last
        // (after manifest write succeeds). Returns a non-null Response.
        _mockAppendBlob
            .Setup(c => c.DeleteAsync(
                It.IsAny<DeleteSnapshotsOption>(),
                It.IsAny<BlobRequestConditions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response>());

        // Archive-side chain: service → container → blob. All three are virtual
        // instance members on the SDK clients (unlike GetAppendBlobClient which
        // was an extension method), so the chain mocks directly.
        _mockBlobService = new Mock<BlobServiceClient>();
        _mockArchiveContainer = new Mock<BlobContainerClient>();
        _mockArchiveBlob = new Mock<BlobClient>();

        _mockBlobService
            .Setup(s => s.GetBlobContainerClient(It.IsAny<string>()))
            .Returns(_mockArchiveContainer.Object);
        _mockArchiveContainer
            .Setup(c => c.GetBlobClient(It.IsAny<string>()))
            .Returns(_mockArchiveBlob.Object);

        // Default UploadAsync stub — returns a non-null Response so awaiters
        // don't NRE. Tests override with a Callback to capture stream bytes.
        _mockArchiveBlob
            .Setup(c => c.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Parquet writer is a field now (not Mock.Of<>) so FlushPartitionAsync
        // tests can configure capture/return behaviour per-test.
        _mockParquetWriter = new Mock<IParquetArchiveWriter>();
        _mockParquetWriter
            .Setup(w => w.WriteAsync(
                It.IsAny<Stream>(),
                It.IsAny<IReadOnlyList<ArchiveEvent>>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var options = Options.Create(new ArchiveOptions { PartitionGraceMinutes = 10 });
        var logger = Mock.Of<ILogger<ArchiverFlushFunction>>();
        _function = new ArchiverFlushFunction(
            _mockPendingStore.Object, _mockBlobService.Object, _mockParquetWriter.Object, options, logger);
    }

    #region Helpers

    // Build a minimal ArchiveEvent for round-trip serialisation tests.
    // Only EventId and shape matter here — actual field values are placeholders.
    private static ArchiveEvent MakeArchiveEvent(string eventId)
    {
        var ts = new DateTimeOffset(2026, 6, 5, 7, 30, 0, TimeSpan.Zero);
        return new ArchiveEvent(
            EventId: eventId,
            EventType: "com.sydneypulse.VehicleUpdate.v1",
            EventVersion: "v1",
            SourceTimestamp: ts, PublishedAt: ts, ArchivedAt: ts.AddSeconds(5),
            RouteShortName: "T1",
            VehicleId: $"VH-{eventId}", TripId: "TRIP-1", RouteId: "NTH_1a",
            RouteLongName: "T1 North Shore", RouteColor: "#F99D1C", Mode: "sydneytrains",
            Latitude: -33.8688, Longitude: 151.2093,
            Bearing: 90f, SpeedKmh: 60f, OccupancyStatus: "MANY_SEATS_AVAILABLE",
            AlertId: null, Severity: null, HeaderText: null,
            DescriptionText: null, StartsAt: null, EndsAt: null);
    }

    // Concatenate events as JSONL — one line per event, "\n" terminator on each.
    // Matches the on-disk shape that ArchiverIngestFunction writes.
    private static string BuildJsonl(params ArchiveEvent[] events) =>
        string.Concat(events.Select(e => JsonSerializer.Serialize(e) + "\n"));

    // Build an IAsyncEnumerable<string> from a fixed list — used to stub
    // IPendingBlobStore.ListPartitionPathsAsync without taking a dependency
    // on System.Linq.Async.
    private static async IAsyncEnumerable<string> AsAsyncEnumerable(params string[] items)
    {
        foreach (var item in items)
        {
            await Task.Yield();
            yield return item;
        }
    }

    // Build a minimal ArchiveManifest for the WriteManifest tests.
    // The partitionPath field is the one assertion target; other fields are
    // placeholders so the manifest is well-formed.
    private static ArchiveManifest MakeManifest(string partitionPath, int eventCount = 1)
    {
        var ts = new DateTimeOffset(2026, 6, 5, 7, 30, 0, TimeSpan.Zero);
        return new ArchiveManifest(
            PartitionPath: partitionPath,
            WrittenAt: ts,
            EventCount: eventCount,
            ByteSize: 1024,
            FirstSourceTimestamp: ts,
            LastSourceTimestamp: ts.AddMinutes(59),
            Files: new[]
            {
                new ArchiveManifestFile(
                    FileName: "events-20260605T073000.parquet",
                    EventCount: eventCount,
                    ByteSize: 1024)
            });
    }

    // Wire the mock AppendBlobClient to return the supplied JSONL when downloaded.
    // Uses BlobsModelFactory to build a real BlobDownloadResult instance — Moq alone
    // can't construct the SDK's internal model types.
    private void SetupDownloadContent(string jsonl)
    {
        var downloadResult = BlobsModelFactory.BlobDownloadResult(
            content: BinaryData.FromString(jsonl));
        _mockAppendBlob
            .Setup(c => c.DownloadContentAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(Response.FromValue(downloadResult, Mock.Of<Response>()));
    }

    #endregion

    #region ListCloseablePartitions

    // Happy path: three partitions for hours 07, 08, 09. At now=09:35 with
    // grace=10min, hours 07 and 08 are both safely past grace (ended at 08:00
    // and 09:00 respectively); hour 09 is still in progress. Pins the basic
    // filter.
    [Fact]
    public void ListCloseablePartitions_ThreeHourPartitions_ReturnsOnlyThoseWhoseHourEndedPastGrace()
    {
        // Arrange
        var partitions = new[]
        {
            "yyyy=2026/MM=06/dd=05/HH=07",
            "yyyy=2026/MM=06/dd=05/HH=08",
            "yyyy=2026/MM=06/dd=05/HH=09",
        };
        var now = new DateTimeOffset(2026, 6, 5, 9, 35, 0, TimeSpan.Zero);

        // Act
        var result = _function.ListCloseablePartitions(partitions, now).ToList();

        // Assert — 07 and 08 closeable; 09 (current hour) still in progress
        Assert.Equal(2, result.Count);
        Assert.Contains("yyyy=2026/MM=06/dd=05/HH=07", result);
        Assert.Contains("yyyy=2026/MM=06/dd=05/HH=08", result);
        Assert.DoesNotContain("yyyy=2026/MM=06/dd=05/HH=09", result);
    }

    // Grace boundary - JUST ELAPSED. Hour 08 ended at 09:00 UTC. At now=09:10
    // with grace=10min, the partition has crossed grace by EXACTLY 0 seconds.
    // The contract is inclusive (<=): partition.endsAt <= now - grace → closeable.
    // If this test fails the implementation likely uses < instead of <=.
    [Fact]
    public void ListCloseablePartitions_PartitionExactlyAtGraceBoundary_IsCloseable()
    {
        // Arrange — hour 08 partition; now is precisely grace-minutes past its end
        var partitions = new[] { "yyyy=2026/MM=06/dd=05/HH=08" };
        var now = new DateTimeOffset(2026, 6, 5, 9, 10, 0, TimeSpan.Zero);

        // Act
        var result = _function.ListCloseablePartitions(partitions, now).ToList();

        // Assert
        Assert.Single(result);
        Assert.Contains("yyyy=2026/MM=06/dd=05/HH=08", result);
    }

    // Grace boundary - NOT YET. Hour 09 ends at 10:00 UTC. At now=09:30 with
    // grace=10min, hour 09 is still actively being written to by Ingest.
    // Pins the "never finalise a live partition" guarantee — flushing a still-
    // open partition would mean events written after the flush are stranded
    // in pending forever.
    [Fact]
    public void ListCloseablePartitions_PartitionStillInProgress_IsNotCloseable()
    {
        // Arrange — hour 09 partition; now is INSIDE that hour
        var partitions = new[] { "yyyy=2026/MM=06/dd=05/HH=09" };
        var now = new DateTimeOffset(2026, 6, 5, 9, 30, 0, TimeSpan.Zero);

        // Act
        var result = _function.ListCloseablePartitions(partitions, now).ToList();

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region ReadPendingEvents

    // Happy path: a JSONL blob with three events round-trips through deserialisation
    // and the resolver is asked for the correct blob path (partition + events.jsonl).
    [Fact]
    public async Task ReadPendingEvents_ThreeEvents_DeserialisesAndReturnsAllAtCorrectPath()
    {
        // Arrange
        var partitionPath = "yyyy=2026/MM=06/dd=05/HH=07";
        var events = new[]
        {
            MakeArchiveEvent("evt-1"),
            MakeArchiveEvent("evt-2"),
            MakeArchiveEvent("evt-3"),
        };
        SetupDownloadContent(BuildJsonl(events));

        // Act
        var result = await _function.ReadPendingEvents(partitionPath, CancellationToken.None);

        // Assert — count + order + EventIds preserved
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "evt-1", "evt-2", "evt-3" }, result.Select(e => e.EventId));

        // Assert — store was asked for the blob at {partitionPath}/events.jsonl
        var expectedPath = $"{partitionPath}/{FunctionConstants.PendingEventsBlobName}";
        _mockPendingStore.Verify(s => s.GetAppendBlob(expectedPath), Times.Once);
    }

    // Trailing newline tolerated. Ingest appends "line + \n" per event, so the
    // blob always ends with "\n". A naive split would produce a phantom empty
    // entry; this test pins that we drop empty lines before deserialising.
    [Fact]
    public async Task ReadPendingEvents_TrailingNewline_DoesNotProduceEmptyEntry()
    {
        // Arrange — content ends with "\n" (normal Ingest output shape)
        var events = new[] { MakeArchiveEvent("evt-only") };
        var jsonl = BuildJsonl(events);
        Assert.EndsWith("\n", jsonl);                 // sanity: matches Ingest's "line + \n"
        SetupDownloadContent(jsonl);

        // Act
        var result = await _function.ReadPendingEvents(
            "yyyy=2026/MM=06/dd=05/HH=08", CancellationToken.None);

        // Assert — exactly one event, no phantom from the trailing "\n"
        Assert.Single(result);
        Assert.Equal("evt-only", result[0].EventId);
    }

    // Duplicates pass through unchanged. EG redelivery causes legitimate
    // duplicates in the JSONL (same EventId on each retry attempt); per the
    // Section 7 durability triangle, ReadPendingEvents stays raw — dedup is
    // FlushPartitionAsync's job. Pin that boundary so a future "helpful"
    // optimisation can't silently fold dedup in here.
    [Fact]
    public async Task ReadPendingEvents_DuplicateEventIds_PassedThroughUnchanged()
    {
        // Arrange — two events with the same EventId
        var events = new[]
        {
            MakeArchiveEvent("evt-dup"),
            MakeArchiveEvent("evt-dup"),
        };
        SetupDownloadContent(BuildJsonl(events));

        // Act
        var result = await _function.ReadPendingEvents(
            "yyyy=2026/MM=06/dd=05/HH=09", CancellationToken.None);

        // Assert — both kept (NOT 1) — dedup belongs in FlushPartitionAsync
        Assert.Equal(2, result.Count);
        Assert.All(result, e => Assert.Equal("evt-dup", e.EventId));
    }

    #endregion

    #region DedupeByEventId

    // Pure function contract: any duplicate-EventId entries collapse to a single
    // representative; non-duplicates pass through; original ordering of unique
    // events is preserved (GroupBy.First semantics).
    [Fact]
    public void DedupeByEventId_DuplicatesAndUniques_KeepsOneCopyPerEventId()
    {
        // Arrange — 4 events, 2 of which share EventId "evt-dup"
        var events = new[]
        {
            MakeArchiveEvent("evt-1"),
            MakeArchiveEvent("evt-dup"),
            MakeArchiveEvent("evt-2"),
            MakeArchiveEvent("evt-dup"),
        };

        // Act
        var result = ArchiverFlushFunction.DedupeByEventId(events);

        // Assert — 3 distinct EventIds, all expected ones present
        Assert.Equal(3, result.Count);
        Assert.Equal(new[] { "evt-1", "evt-dup", "evt-2" }, result.Select(e => e.EventId));
    }

    #endregion

    #region WriteManifest

    // Happy path: serialises the manifest as JSON and uploads it as a block
    // blob to {ArchiveContainer}/{partitionPath}/_manifest.json with
    // overwrite=true. Pins container choice, blob path composition,
    // overwrite semantics, and that the serialised payload reaches the wire.
    [Fact]
    public async Task WriteManifest_HappyPath_WritesJsonToArchivePathWithOverwrite()
    {
        // Arrange
        var partitionPath = "yyyy=2026/MM=06/dd=05/HH=07";
        var manifest = MakeManifest(partitionPath, eventCount: 42);

        // Capture the uploaded bytes so we can assert payload shape
        byte[]? capturedBytes = null;
        _mockArchiveBlob
            .Setup(c => c.UploadAsync(It.IsAny<Stream>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .Callback<Stream, bool, CancellationToken>((stream, _, _) =>
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                capturedBytes = ms.ToArray();
            })
            .ReturnsAsync(Mock.Of<Response<BlobContentInfo>>());

        // Act
        await _function.WriteManifest(partitionPath, manifest, CancellationToken.None);

        // Assert — container resolved is "archive"
        _mockBlobService.Verify(s =>
            s.GetBlobContainerClient(FunctionConstants.ArchiveDataLakeContainer), Times.Once);

        // Assert — blob path is {partitionPath}/_manifest.json
        var expectedPath = $"{partitionPath}/{FunctionConstants.ArchiveManifestBlobName}";
        _mockArchiveContainer.Verify(c =>
            c.GetBlobClient(expectedPath), Times.Once);

        // Assert — uploaded exactly once with overwrite=true (idempotency contract)
        _mockArchiveBlob.Verify(c =>
            c.UploadAsync(It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()), Times.Once);

        // Assert — payload contains the partition path (sanity: we serialised
        // the right object, not an empty stream)
        Assert.NotNull(capturedBytes);
        var json = Encoding.UTF8.GetString(capturedBytes!);
        Assert.Contains(partitionPath, json);
    }

    #endregion

    #region FlushPartitionAsync

    // End-to-end orchestration: given a partition with 3 raw events (1 duplicate),
    // FlushPartitionAsync should
    //   - dedupe by EventId so the Parquet writer sees 2 unique events
    //   - upload Parquet to archive/{partitionPath}/events.parquet (overwrite=true)
    //   - write the manifest at archive/{partitionPath}/_manifest.json
    //   - delete the pending blob exactly once
    // This single test pins the whole pipeline.
    [Fact]
    public async Task FlushPartitionAsync_HappyPath_DedupesWritesParquetWritesManifestDeletesPending()
    {
        // Arrange — 3 events, 1 duplicate (evt-1 twice)
        var partitionPath = "yyyy=2026/MM=06/dd=05/HH=07";
        var rawEvents = new[]
        {
            MakeArchiveEvent("evt-1"),
            MakeArchiveEvent("evt-2"),
            MakeArchiveEvent("evt-1"),
        };
        SetupDownloadContent(BuildJsonl(rawEvents));

        // Capture the events Parquet writer receives so we can pin the dedup contract.
        // The Callback also writes some bytes to the buffer so the manifest's ByteSize
        // ends up > 0 (more representative of the real path).
        IReadOnlyList<ArchiveEvent>? eventsToParquet = null;
        _mockParquetWriter
            .Setup(w => w.WriteAsync(
                It.IsAny<Stream>(),
                It.IsAny<IReadOnlyList<ArchiveEvent>>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, IReadOnlyList<ArchiveEvent>, CancellationToken>((stream, events, _) =>
            {
                eventsToParquet = events;
                stream.Write(new byte[1024], 0, 1024);
            })
            .Returns(Task.CompletedTask);

        // Act
        await _function.FlushPartitionAsync(partitionPath, CancellationToken.None);

        // Assert — dedup applied: Parquet writer got 2 unique events, not 3
        Assert.NotNull(eventsToParquet);
        Assert.Equal(2, eventsToParquet!.Count);
        Assert.Contains(eventsToParquet, e => e.EventId == "evt-1");
        Assert.Contains(eventsToParquet, e => e.EventId == "evt-2");

        // Assert — Parquet uploaded to events.parquet path (in archive container)
        var expectedParquetPath = $"{partitionPath}/{FunctionConstants.ArchiveEventsBlobName}";
        _mockArchiveContainer.Verify(c => c.GetBlobClient(expectedParquetPath), Times.Once);

        // Assert — Manifest uploaded to _manifest.json path
        var expectedManifestPath = $"{partitionPath}/{FunctionConstants.ArchiveManifestBlobName}";
        _mockArchiveContainer.Verify(c => c.GetBlobClient(expectedManifestPath), Times.Once);

        // Assert — Two uploads against the archive blob mock (Parquet + manifest)
        _mockArchiveBlob.Verify(c => c.UploadAsync(
            It.IsAny<Stream>(), true, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Assert — Pending blob deleted exactly once (the cleanup, only after manifest)
        _mockAppendBlob.Verify(c => c.DeleteAsync(
            It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region RunAsync (timer trigger orchestration)

    // End-to-end orchestration: given two partitions — one definitively closeable
    // (far in the past) and one definitively in progress (far in the future) —
    // RunAsync should flush only the closeable one and leave the other untouched.
    //
    // Using paths anchored to fixed wall-clock dates keeps the test deterministic
    // regardless of when it runs (the grace check against DateTimeOffset.UtcNow
    // resolves the same way for inputs ±decades from "now").
    [Fact]
    public async Task RunAsync_TwoPartitions_FlushesOnlyTheClosableOne()
    {
        // Arrange — partition 2020 ends in 2020-01-01 01:00 UTC; always closeable.
        // Partition 2099 ends in 2100-01-01 00:00 UTC; never closeable.
        var closeable = "yyyy=2020/MM=01/dd=01/HH=00";
        var inProgress = "yyyy=2099/MM=12/dd=31/HH=23";

        _mockPendingStore
            .Setup(s => s.ListPartitionPathsAsync(It.IsAny<CancellationToken>()))
            .Returns(AsAsyncEnumerable(closeable, inProgress));

        // Set up the pending download for whichever partition gets flushed —
        // any partition path resolves to the same mock AppendBlobClient, and
        // the download returns a minimal one-event JSONL.
        SetupDownloadContent(BuildJsonl(MakeArchiveEvent("evt-1")));

        // Act — TimerInfo is the trigger parameter; its value doesn't influence
        // the orchestration logic (RunAsync captures DateTimeOffset.UtcNow itself).
        await _function.RunAsync(new TimerInfo(), CancellationToken.None);

        // Assert — closeable partition's manifest path was resolved (flush ran)
        var closeableManifestPath = $"{closeable}/{FunctionConstants.ArchiveManifestBlobName}";
        _mockArchiveContainer.Verify(c => c.GetBlobClient(closeableManifestPath), Times.Once);

        // Assert — in-progress partition's manifest path was NEVER resolved
        var inProgressManifestPath = $"{inProgress}/{FunctionConstants.ArchiveManifestBlobName}";
        _mockArchiveContainer.Verify(c => c.GetBlobClient(inProgressManifestPath), Times.Never);

        // Assert — exactly one DeleteAsync (only the closeable partition got cleaned up)
        _mockAppendBlob.Verify(c => c.DeleteAsync(
            It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion
}
