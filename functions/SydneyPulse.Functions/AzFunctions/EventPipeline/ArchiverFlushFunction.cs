// ArchiverFlushFunction.cs
// ------------------------
// Timer trigger every 5 minutes. Finalises closeable partition hours into Parquet
// files + writes the manifest (ADR-0012).
//
// Closeable = the hour ended at least PartitionGraceMinutes ago. Late events
// inside the grace window still land via ArchiverIngestFunction.
//
// Idempotency: a partition's pending blob is only deleted AFTER the manifest
// write succeeds. If we crash between Parquet write and manifest write, the next
// tick re-flushes the partition (overwriting Parquet) and then writes manifest.
// The cost is one duplicate Parquet write on rare crashes; analytics tools that
// honour the manifest see consistent state.

using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.Archive;
using SydneyPulse.Functions.Archive;
using System.Text;
using System.Text.Json;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

// `pendingStore` handles reads/deletes on the "pending" container; `blobService`
// is retained for the "archive" container (Parquet + manifest writes) until a
// dedicated IArchiveBlobStore lands later.
public class ArchiverFlushFunction(
    IPendingBlobStore pendingStore,
    BlobServiceClient blobService,
    IParquetArchiveWriter parquetWriter,
    IOptions<ArchiveOptions> archiveOptions,
    ILogger<ArchiverFlushFunction> logger)
{
    private readonly ArchiveOptions _opts = archiveOptions.Value;

    // RunAsync: orchestration — list closeable partitions, flush each one.
    // NCRONTAB cadence is hard-coded "0 */5 * * * *" (every 5 min, at :00 :05 :10 …).
    // ArchiveOptions.FlushIntervalMinutes is informational; runtime parameterisation
    // of NCRONTAB is not supported.
    // Estimated implementation time: 10 min.
    [Function("ArchiverFlush")]
    public async Task RunAsync(
        [TimerTrigger("0 */5 * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        // 1. capture "now" once so the closeable check is consistent across this tick
        var now = DateTimeOffset.UtcNow;

        // 2. enumerate every partition the pending container currently holds
        var allPartitions = new List<string>();
        await foreach (var p in pendingStore.ListPartitionPathsAsync(cancellationToken))
            allPartitions.Add(p);

        // 3. filter to those past the grace window
        // Materialised to a list so we can log Count and still enumerate in the foreach below.
        var closeable = ListCloseablePartitions(allPartitions, now).ToList();

        logger.LogInformation(
            "Flush tick: {Total} pending partition(s), {Closeable} closeable past {Grace}-min grace.",
            allPartitions.Count, closeable.Count, _opts.PartitionGraceMinutes);

        // 4. flush each — sequentially is fine at our event volume
        foreach (var partition in closeable)
        {
            await FlushPartitionAsync(partition, cancellationToken);
        }
    }

    // ListCloseablePartitions: pure filter — given a sequence of partition paths
    // and the current time, return those whose hour ENDED at least
    // PartitionGraceMinutes ago. Partition listing (the I/O side) is a separate
    // concern handled by IPendingBlobStore.
    //
    // Semantics: a partition at hour H ends at H+1:00. It's closeable when
    //     (H + 1 hour) <= (now - PartitionGraceMinutes)
    // i.e. the hour has not only ended but the grace window has also elapsed.
    //
    // `internal` so the unit test in SydneyPulse.Tests can drive it directly,
    // matching the MapToArchiveEvent + ParquetArchiveWriter helper pattern.
    internal IEnumerable<string> ListCloseablePartitions(
        IEnumerable<string> partitionPaths,
        DateTimeOffset now)
    {
        // Partition becomes closeable at: hour-end + grace-window. If `now` has
        // reached that moment, no more JSONL is accepted to parition → safe to close & flush.
        return partitionPaths.Where(x => 
            HivePartitionPath.Parse(x)
            .AddHours(1)
            .AddMinutes(_opts.PartitionGraceMinutes) <= now);
    }

    // FlushPartitionAsync: end-to-end flush of one partition.
    //   1. ReadPendingEvents from pending/{partitionPath}/events.jsonl
    //   2. DedupeByEventId  — Section 7 durability triangle's read-time filter
    //   3. parquetWriter.WriteAsync into a MemoryStream buffer
    //   4. Upload the Parquet bytes to archive/{partitionPath}/events.parquet
    //      (block blob, overwrite=true — idempotent under re-flush)
    //   5. WriteManifest to archive/{partitionPath}/_manifest.json
    //      (THIS is the commit point — see primer Section 8 for why)
    //   6. Delete the pending blob (ONLY after step 5 succeeds)
    //
    // `internal` so the unit test can drive it directly.
    internal async Task FlushPartitionAsync(
        string partitionPath,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Flushing partition {Partition}", partitionPath);

        // 1. read raw events from pending JSONL
        var rawEvents = await ReadPendingEvents(partitionPath, cancellationToken);

        // 2. drop duplicate EventIds (EG retry artefacts).
        var uniqueEvents = DedupeByEventId(rawEvents);

        // 3. write Parquet data into in memory stream (buffer)
        using var parquetStream = new MemoryStream();
        await parquetWriter.WriteAsync(parquetStream, uniqueEvents, cancellationToken);
        var parquetByteSize = parquetStream.Length;
        parquetStream.Position = 0;                     // rewind for upload

        // 4. upload Parquet Stream to archive container — overwrite=true makes re-flush idempotent
        var parquetBlobPath = $"{partitionPath}/{FunctionConstants.ArchiveEventsBlobName}";
        var parquetBlobContainerClient = blobService.GetBlobContainerClient(_opts.ArchiveContainer);
        var parquetBlobClient = parquetBlobContainerClient.GetBlobClient(parquetBlobPath);
        await parquetBlobClient.UploadAsync(parquetStream, overwrite: true, cancellationToken);

        //  5. build the manifest from the just-written Parquet stats
        var manifest = new ArchiveManifest(
                PartitionPath: partitionPath,
                WrittenAt: DateTimeOffset.UtcNow,
                EventCount: uniqueEvents.Count,
                ByteSize: parquetByteSize,
                FirstSourceTimestamp: uniqueEvents.Min(e => e.SourceTimestamp),
                LastSourceTimestamp: uniqueEvents.Max(e => e.SourceTimestamp),
                Files: [
                    new ArchiveManifestFile(
                        FunctionConstants.ArchiveEventsBlobName, 
                        uniqueEvents.Count, 
                        parquetByteSize)
                    ]
            );

        // 6. THE COMMIT POINT — manifest write marks the partition queryable
        await WriteManifest(partitionPath, manifest, cancellationToken);

        logger.LogInformation(
            "Committed partition {Partition}: {EventCount} events, {ByteSize} bytes.",
            partitionPath, uniqueEvents.Count, parquetByteSize);

        // 7. only NOW delete pending blob (cleanup that depends on commit)
        var pendingBlobPath = $"{partitionPath}/{FunctionConstants.PendingEventsBlobName}";
        var pendingBlobClient = pendingStore.GetAppendBlob(pendingBlobPath);
        await pendingBlobClient.DeleteAsync(
            snapshotsOption: DeleteSnapshotsOption.None, 
            conditions: null, 
            cancellationToken: cancellationToken);
    }

    // DedupeByEventId: pure filter — collapses any duplicate EventId entries
    // to a single representative. Duplicates are byte-identical (same EventId
    // is set once by PollerFunction and copied verbatim across EG retries —
    // see primer Section 7), so "keep one" is safe regardless of which.
    //
    // `internal static` because it's pure CPU and trivially testable without
    // any of the Function's instance dependencies.
    internal static IReadOnlyList<ArchiveEvent> DedupeByEventId(
        IEnumerable<ArchiveEvent> events) => 
        [..events
            .GroupBy(e => e.EventId)
            .Select(g => g.First())];

    // ReadPendingEvents: downloads the JSONL blob, deserialises each line as
    // ArchiveEvent, returns the materialised list. Pure I/O + JSON parse —
    // DOES NOT deduplicate. Duplicate-by-EventId filtering happens later in
    // FlushPartitionAsync (per the at-least-once durability triangle in
    // docs/parquet-datalake-primer.md Section 7).
    //
    // Materialises to a list because the Parquet writer needs Count for the
    // row group. For very large partitions consider streaming directly into
    // Parquet — out of scope here.
    //
    // `internal` so the unit test in SydneyPulse.Tests can drive it directly.
    internal async Task<IReadOnlyList<ArchiveEvent>> ReadPendingEvents(
        string partitionPath,
        CancellationToken cancellationToken)
    {
        // construct JSONL blob path
        var jsonlPath = $"{partitionPath}/{FunctionConstants.PendingEventsBlobName}";

        // get jsonL blob client
        var jsonlBlobClient = pendingStore.GetAppendBlob(jsonlPath);

        // download jsonL content as a BlobDownloadResult
        var blobDownloadResult = await jsonlBlobClient.DownloadContentAsync(cancellationToken);

        // jsonL lines
        // JSONL mandates '\n' — Environment.NewLine would break on Windows where it's "\r\n"
        var jsonLines = blobDownloadResult.Value.Content.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);        
       
        // conversion json lines to ArchiveEvent list
        return jsonLines.Select(line => JsonSerializer.Deserialize<ArchiveEvent>(line)).ToList()!;
    }

    // WriteManifest: serialises ArchiveManifest as JSON and writes it as a
    // block blob to {ArchiveContainer}/{partitionPath}/_manifest.json.
    //
    // Idempotent: uses overwrite=true so a re-flush after a crash between
    // Parquet write and manifest write safely replaces the manifest (the
    // file-header idempotency note explains why this matters).
    //
    // Why a block blob (not append): the manifest is REPLACED on each
    // successful flush, not extended. Block blob's overwrite semantics fit.
    //
    // `internal` so the unit test in SydneyPulse.Tests can drive it directly.
    internal async Task WriteManifest(
        string partitionPath,
        ArchiveManifest manifest,
        CancellationToken cancellationToken)
    {
        // manifest blob path
        var manifestFilePath = $"{partitionPath}/{FunctionConstants.ArchiveManifestBlobName}";

        // get manifest blob container client
        var manifestBlobContainerClient = blobService
            .GetBlobContainerClient(_opts.ArchiveContainer);

        // get manifest blob client
        var manifestBlobClient = manifestBlobContainerClient.GetBlobClient(manifestFilePath);

        // serialise manifest to JSON
        var json = JsonSerializer.Serialize(manifest);

        // construct stream from JSON
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        // overwrite=true makes the write idempotent — a re-flush after a crash
        // safely replaces the manifest rather than failing on "blob already exists".
        await manifestBlobClient.UploadAsync(
            stream,
            overwrite: true,
            cancellationToken);
    }
}
