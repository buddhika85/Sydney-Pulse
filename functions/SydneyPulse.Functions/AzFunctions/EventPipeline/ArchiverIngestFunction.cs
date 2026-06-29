// ArchiverIngestFunction.cs
// -------------------------
// Event Grid trigger: receives every VehicleUpdate.v1 and ServiceAlert.v1 CloudEvent
// and appends a JSONL line to the partition's append blob in the Data Lake (ADR-0012).
//
// Crash-safety design — see ADR-0012 Reasoning. Each AppendBlock call is atomic;
// either the event lands or it doesn't. If the Function crashes mid-method, EG
// retries delivery (at-least-once); duplicate JSONL lines are tolerated and
// deduped at read time by EventId column.
//
// Partition key is derived from event.SourceTimestamp (not DateTimeOffset.UtcNow)
// so late events land in the correct hour partition.

using System.Text;
using System.Text.Json;
using Azure.Messaging;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Archive;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions.Archive;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

// Ingest takes no IOptions<ArchiveOptions> — its only Archive-config need
// (the pending container name) is encapsulated inside PendingBlobStore.
// ArchiveOptions is consumed by Program.cs (DataLakeAccountName) and by
// ArchiverFlushFunction (grace/batch/archive-container settings).
public class ArchiverIngestFunction(
    IPendingBlobStore pendingStore,
    ILogger<ArchiverIngestFunction> logger)
{
    // RunAsync: orchestration — map → append. Keeps the trigger method <20 lines
    // per functions/CLAUDE.md "Functions are thin" rule.
    // Estimated implementation time: 10 min.
    [Function("ArchiverIngest")]
    public async Task RunAsync(
        [EventGridTrigger] CloudEvent cloudEvent,
        CancellationToken cancellationToken)
    {
        var archivedAt = DateTimeOffset.UtcNow;          // capture once per invocation

        // convert cloud event to common ArchiveEvent shape,
        // identifying service alert vs vehicle update by cloudEvent.Type and mapping fields 
        var archiveEvent = MapToArchiveEvent(cloudEvent, archivedAt);

        // persist the event as a JSON line in the pending blob for the hour partition
        await AppendToPendingAsync(archiveEvent, cancellationToken);

        logger.LogInformation("Archived event {EventId} to partition {Hour}",
          archiveEvent.EventId, HivePartitionPath.ForHour(archiveEvent.SourceTimestamp));
    }

    // MapToArchiveEvent: discriminates by cloudEvent.Type, deserialises payload
    // as the matching event record (VehicleUpdate or ServiceAlert), and projects
    // to the unified ArchiveEvent. Type-specific fields are null for the other type.
    //
    // Pure function — archivedAt is passed in so the method has no DateTimeOffset.UtcNow
    // dependency and is easy to test. RunAsync captures UtcNow once and passes through.
    //
    // SourceTimestamp resolution:
    //   - VehicleUpdate.v1 → update.VehicleTimestamp
    //   - ServiceAlert.v1  → alert.StartsAt ?? cloudEvent.Time ?? archivedAt
    internal static ArchiveEvent MapToArchiveEvent(
        CloudEvent cloudEvent,
        DateTimeOffset archivedAt)
    {
        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };

        return cloudEvent.Type switch
        {
            FunctionConstants.VehicleUpdateEventType => MapVehicleUpdate(cloudEvent, archivedAt, jsonOptions),
            FunctionConstants.ServiceAlertEventType => MapServiceAlert(cloudEvent, archivedAt, jsonOptions),
            _ => throw new InvalidOperationException($"Unsupported CloudEvent type: {cloudEvent.Type}")
        };

    }

    private static ArchiveEvent MapVehicleUpdate(CloudEvent cloudEvent, DateTimeOffset archivedAt, JsonSerializerOptions jsonOptions)
    {
        var vehicleUpdateEvent = cloudEvent.Data!.ToObjectFromJson<VehicleUpdate>(jsonOptions)
            ?? throw new JsonException("Failed to deserialize CloudEvent JSON data to VehicleUpdate");

        return new ArchiveEvent(
            // Id fields
            EventId: cloudEvent.Id,
            EventType: cloudEvent.Type,
            EventVersion: ExtractVersion(cloudEvent.Type),

            // Timestamp fields
            SourceTimestamp: vehicleUpdateEvent.VehicleTimestamp,
            PublishedAt: cloudEvent.Time ?? archivedAt,
            ArchivedAt: archivedAt,

            // Routing key
            RouteShortName: vehicleUpdateEvent.RouteShortName,

            // Vehicle fields            
            VehicleId: vehicleUpdateEvent.VehicleId,
            TripId: vehicleUpdateEvent.TripId,
            RouteId: vehicleUpdateEvent.RouteId,
            RouteLongName: vehicleUpdateEvent.RouteLongName,
            RouteColor: vehicleUpdateEvent.RouteColor,
            Mode: vehicleUpdateEvent.Mode,
            Latitude: vehicleUpdateEvent.Latitude,
            Longitude: vehicleUpdateEvent.Longitude,
            Bearing: vehicleUpdateEvent.Bearing,
            SpeedKmh: vehicleUpdateEvent.SpeedKmh,
            OccupancyStatus: vehicleUpdateEvent.OccupancyStatus,

            // ServiceAlert fields null for vehicle updates
            AlertId: null,
            Severity: null,
            HeaderText: null,
            DescriptionText: null,
            StartsAt: null,
            EndsAt: null);
    }



    private static ArchiveEvent MapServiceAlert(CloudEvent cloudEvent, DateTimeOffset archivedAt, JsonSerializerOptions jsonOptions)
    {
        var serviceAlertEvent = cloudEvent.Data!.ToObjectFromJson<ServiceAlert>(jsonOptions)
            ?? throw new JsonException("Failed to deserialize CloudEvent JSON data to ServiceAlert");

        return new ArchiveEvent(
            // Id fields
            EventId: cloudEvent.Id,
            EventType: cloudEvent.Type,
            EventVersion: ExtractVersion(cloudEvent.Type),

            // Timestamp fields
            SourceTimestamp: serviceAlertEvent.StartsAt ?? cloudEvent.Time ?? archivedAt,
            PublishedAt: cloudEvent.Time ?? archivedAt,
            ArchivedAt: archivedAt,

            // Routing key
            RouteShortName: serviceAlertEvent.RouteShortName,

            // Vehicle fields are null for service alerts            
            VehicleId: null,
            TripId: null,
            RouteId: null,
            RouteLongName: null,
            RouteColor: null,
            Mode: null,
            Latitude: null,
            Longitude: null,
            Bearing: null,
            SpeedKmh: null,
            OccupancyStatus: null,

            // ServiceAlert fields
            AlertId: serviceAlertEvent.AlertId,
            Severity: serviceAlertEvent.Severity,
            HeaderText: serviceAlertEvent.HeaderText,
            DescriptionText: serviceAlertEvent.DescriptionText,
            StartsAt: serviceAlertEvent.StartsAt,
            EndsAt: serviceAlertEvent.EndsAt);
    }

    // A helper to extract version from event type string
    // example eventType "com.sydneypulse.VehicleUpdate.v1" → returns "v1"
    private static string ExtractVersion(string eventType) =>
     eventType[(eventType.LastIndexOf('.') + 1)..];

    // AppendToPendingAsync: writes one JSONL line to the partition's pending blob.
    // Blob path: {pendingContainer}/{HivePartitionPath.ForHour(SourceTimestamp)}/events.jsonl
    // Calls CreateIfNotExistsAsync before each append; idempotent — only the first
    // call per blob actually creates, subsequent calls are no-ops (safe under
    // concurrent ingest from multiple Function instances).
    // Then AppendBlobClient.AppendBlockAsync writes the JSONL line + newline.
    // `internal` so the unit test in SydneyPulse.Tests can drive it directly
    // (mirrors the MapToArchiveEvent + ParquetArchiveWriter helpers pattern).
    internal async Task AppendToPendingAsync(
        ArchiveEvent archiveEvent,
        CancellationToken cancellationToken)
    {
        // path for the blob is derived from the event's SourceTimestamp (occurrence time), not from archivedAt or UtcNow
        // so late events land in the correct partition.
        var path = $"{HivePartitionPath.ForHour(archiveEvent.SourceTimestamp)}/{FunctionConstants.PendingEventsBlobName}";
        var blob = pendingStore.GetAppendBlob(path);

        // CreateIfNotExistsAsync is idempotent and concurrency-safe;
        // only the first call for a given blob creates it, subsequent calls are no-ops.
        await blob.CreateIfNotExistsAsync(options: null, cancellationToken);

        // JSONL = one JSON object per line. Newline terminator matters for downstream readers.
        var jsonLine = JsonSerializer.Serialize(archiveEvent) + "\n";

        // Convert the JSON line to a stream and append to the blob.        
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(jsonLine));

        // AppendBlockAsync is atomic — see the file-header crash-safety note for
        // why at-least-once delivery + duplicate tolerance is safe here.
        await blob.AppendBlockAsync(stream, cancellationToken: cancellationToken);
    }
}
