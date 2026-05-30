// Cosmos DB document for a vehicle's latest position.
// Partition key: routeShortName (e.g. "T1"). Container TTL is 5 minutes (set at container level).
// CosmosClient is configured with CamelCase serialization — property names map directly to JSON.
// "Id" → "id" satisfies Cosmos's required lowercase id field.

namespace SydneyPulse.Core.Cosmos;

public record VehicleDocument
{
    // Cosmos document id — one document per vehicle; upsert overwrites the previous position.
    public required string Id { get; init; }

    // Partition key — must match the container's partition key path (/routeShortName).
    public required string RouteShortName { get; init; }

    public required string VehicleId { get; init; }

    public double Latitude { get; init; }

    public double Longitude { get; init; }

    // Degrees 0–359; null when not reported by the feed.
    public float? Bearing { get; init; }

    // Km/h; null when not reported.
    public float? SpeedKmh { get; init; }

    // Internal TfNSW route id (e.g. "NTH_1a") — kept for traceability.
    public required string RouteId { get; init; }

    // Feed timestamp for this vehicle position — used for stale-write guard.
    public DateTimeOffset Timestamp { get; init; }

    // Wall-clock time this document was written — used for observability.
    public DateTimeOffset UpdatedAt { get; init; }
}
