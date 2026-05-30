// AlertDocument.cs
// ----------------
// Cosmos DB document model for the alerts container.
// One document per active alert; upsert overwrites on repeat delivery.
// Partition key: routeShortName (matches VehicleDocument and ADR-0002 pattern).
// TTL: 24 hours, set by Cosmos container policy — no code needed to expire alerts.

namespace SydneyPulse.Core.Cosmos;

public record AlertDocument
{
    // Cosmos requires lowercase "id" — CamelCase policy on CosmosClient handles this
    public required string Id { get; init; }             // = alertId
    public required string RouteShortName { get; init; } // partition key

    // Id = alertId 
    public required string AlertId { get; init; }

    public required string Severity { get; init; }       // e.g. "significant_delays"
    public required string HeaderText { get; init; }     // short summary for UI
    public required string DescriptionText { get; init; }
    public DateTimeOffset? StartsAt { get; init; }
    public DateTimeOffset? EndsAt { get; init; }         // null = no defined end
    public DateTimeOffset ReceivedAt { get; init; }      // when the Function processed it
}
