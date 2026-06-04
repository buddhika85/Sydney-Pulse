// ArchiverIngestFunction.cs
// -------------------------
// Event Grid trigger: receives every VehicleUpdate.v1 and ServiceAlert.v1 CloudEvent
// and appends a JSONL line to the partition's pending blob in the Data Lake (ADR-0012).
//
// Crash-safety design — see ADR-0012 Reasoning. Each AppendBlock call is atomic;
// either the event lands or it doesn't. If the Function crashes mid-method, EG
// retries delivery (at-least-once); duplicate JSONL lines are tolerated and
// deduped at read time by EventId column.
//
// Partition key is derived from event.SourceTimestamp (not DateTimeOffset.UtcNow)
// so late events land in the correct hour partition.

using System.Text.Json;
using Azure.Messaging;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Specialized;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.Archive;
using SydneyPulse.Core.Events;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

public class ArchiverIngestFunction(
    BlobServiceClient blobService,
    IOptions<ArchiveOptions> archiveOptions,
    ILogger<ArchiverIngestFunction> logger)
{
    // Cached options snapshot — DI gives a singleton IOptions; .Value is a one-time read.
    private readonly ArchiveOptions _opts = archiveOptions.Value;

    // RunAsync: orchestration — map → append. Keeps the trigger method <20 lines
    // per functions/CLAUDE.md "Functions are thin" rule.
    // Estimated implementation time: 10 min.
    [Function("ArchiverIngest")]
    public async Task RunAsync(
        [EventGridTrigger] CloudEvent cloudEvent,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    // MapToArchiveEvent: discriminates by cloudEvent.Type, deserialises payload
    // as the matching event record (VehicleUpdate or ServiceAlert), and projects
    // to the unified ArchiveEvent. Type-specific fields are null for the other type.
    //
    // SourceTimestamp resolution:
    //   - VehicleUpdate.v1 → update.VehicleTimestamp
    //   - ServiceAlert.v1  → alert.StartsAt ?? cloudEvent.Time ?? UtcNow
    //
    // Estimated implementation time: 20 min.
    private static ArchiveEvent MapToArchiveEvent(CloudEvent cloudEvent)
    {
        throw new NotImplementedException();
    }

    // AppendToPendingAsync: writes one JSONL line to the partition's pending blob.
    // Blob path: pending/{HivePartitionPath.ForHour(SourceTimestamp)}/events.jsonl
    // Calls AppendBlobClient.CreateIfNotExistsAsync on the first append for a partition
    // (idempotent — safe under concurrent ingest from multiple Function instances).
    // Then AppendBlobClient.AppendBlockAsync writes the JSONL line + newline.
    // Estimated implementation time: 10 min.
    private async Task AppendToPendingAsync(
        ArchiveEvent archiveEvent,
        CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}
