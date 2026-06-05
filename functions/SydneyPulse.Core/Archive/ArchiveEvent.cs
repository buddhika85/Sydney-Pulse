// ArchiveEvent.cs
// ---------------
// Unified Parquet row schema for the archive Data Lake (ADR-0012).

// Expose internals to the test project so ParquetArchiveWriter's internal
// helpers (BuildSchema, BuildColumns) can be unit-tested in isolation.
// Same pattern as SydneyPulse.Functions / FunctionConstants.cs.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SydneyPulse.Tests")]

//
// One record shape covers both VehicleUpdate.v1 and ServiceAlert.v1.
// Type-specific fields are nullable. EventType + EventVersion act as the
// discriminator so analytics queries filter cleanly.
//
// Naming note: the ADR/spec calls the primary timestamp "vehicleTimestamp".
// We use SourceTimestamp here because the schema serves alerts too — a
// "vehicleTimestamp" field on an alert row would mislead future readers.
// SourceTimestamp = the event's source-observation moment:
//   - VehicleUpdate.v1 → VehicleTimestamp (GPS time)
//   - ServiceAlert.v1  → StartsAt ?? PublishedAt
//
// Estimated implementation time: 15 min (record definition + comments).

namespace SydneyPulse.Core.Archive;

public sealed record ArchiveEvent(
    // ─── Identity & discrimination ───────────────────────────────────────
    string EventId,                    // CloudEvent.id — used for read-time dedup
    string EventType,                  // "VehicleUpdate.v1" | "ServiceAlert.v1"
    string EventVersion,               // "v1" — extracted from EventType suffix

    // ─── Three timestamps (every row) ────────────────────────────────────
    DateTimeOffset SourceTimestamp,    // when the event happened at the source
    DateTimeOffset PublishedAt,        // CloudEvent.time — when our pipeline received it
    DateTimeOffset ArchivedAt,         // wall-clock when ArchiverFlushFunction wrote the row

    // ─── Routing key (every row) ─────────────────────────────────────────
    string RouteShortName,             // partition column candidate; used for Hive partitioning if extended

    // ─── VehicleUpdate fields (null for alerts) ──────────────────────────
    string? VehicleId,
    string? TripId,
    string? RouteId,                   // TfNSW internal id, e.g. "NTH_1a" — analytics tracing only
    string? RouteLongName,
    string? RouteColor,                // hex with leading #
    string? Mode,                      // "sydneytrains" | "buses" | "ferries" | "metro" | "lightrail"
    double? Latitude,
    double? Longitude,
    float? Bearing,
    float? SpeedKmh,
    string? OccupancyStatus,

    // ─── ServiceAlert fields (null for vehicles) ─────────────────────────
    string? AlertId,
    string? Severity,                  // "significant_delays" | "no_service" | "reduced_service" etc.
    string? HeaderText,                // short summary
    string? DescriptionText,           // full detail
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt
);
