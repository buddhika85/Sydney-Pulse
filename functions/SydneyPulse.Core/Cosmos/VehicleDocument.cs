// Cosmos DB document for a vehicle's latest position.
// Partition key: routeShortName (e.g. "T1"). Container TTL is 5 minutes (set at container level).
// CosmosClient is configured with CamelCase serialization — property names map directly to JSON.
// "Id" → "id" satisfies Cosmos's required lowercase id field.

namespace SydneyPulse.Core.Cosmos;

public record VehicleDocument
{
    // Cosmos document id — one document per vehicle; upsert overwrites the previous position.
    // Id is same as VehicleId property - but for DDD separation we store 2  
    public required string Id { get; init; }

    // Partition key — must match the container's partition key path (/routeShortName)
    // The Angular dashboard's primary query is GET /api/vehicles?mode=trains,
    // which maps to a set of route short names (T1, T2, T3…)
    // so, routeShortName is the partition key because it groups vehicles by route, aligns with
    // the UI’s query pattern, avoids cross‑partition queries, and keeps RU costs low.
    public required string RouteShortName { get; init; }

    // VehicleId is same as Id property - but for DDD separation we store 2  
    public required string VehicleId { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    // Degrees 0–359; null when not reported by the feed.
    public float? Bearing { get; init; }

    // Km/h; null when not reported.
    public float? SpeedKmh { get; init; }

    // Internal TfNSW route id (e.g. "NTH_1a") — kept for traceability.
    public required string RouteId { get; init; }

    // Denormalized from the VehicleUpdate event — stored so HTTP API reads
    // are single-document lookups with no join (ADR-0011).
    public required string RouteLongName { get; init; }
    public required string RouteColor { get; init; }    // hex with leading #, e.g. "#F99D1C"
    public required string Mode { get; init; }          // e.g. "sydneytrains"
    public string? OccupancyStatus { get; init; }       // null when not reported by the feed

    // Feed timestamp for this vehicle position — used for stale-write guard.
    // 2026-05-30T10:39:00+10:00 -> date + time + an explicit UTC offset.+10 for Sydney
    public DateTimeOffset Timestamp { get; init; }

    // Wall-clock time this document was written — used for observability.
    public DateTimeOffset UpdatedAt { get; init; }
}
