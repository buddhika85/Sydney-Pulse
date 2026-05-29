// ITfNswFeedClient.cs
// -------------------
// Defines the operations for retrieving TfNSW data used by Sydney Pulse.
// The implementation handles:
//   • Downloading GTFS‑Realtime (.pb) feeds
//   • Parsing Protobuf messages
//   • Enriching data with route metadata
//   • Caching GTFS static route info for 1 hour
//
// Registered as a singleton so the route cache is shared across all
// Function App executions within the same host instance.

using SydneyPulse.Core.Events;

namespace SydneyPulse.Core.TfNsw;

public interface ITfNswFeedClient
{
    // Returns the latest vehicle positions for the given mode
    // (e.g., "sydneytrains", "buses"), enriched with route details.
    Task<IReadOnlyList<VehicleUpdate>> GetVehiclePositionsAsync(
        string mode, CancellationToken cancellationToken = default);

    // Returns the latest service alerts for the given mode.
    Task<IReadOnlyList<ServiceAlert>> GetServiceAlertsAsync(
        string mode, CancellationToken cancellationToken = default);

    // Returns route metadata (short name, long name, colour, etc.)
    // from the 1‑hour in‑memory cache. Downloads and parses the
    // GTFS static ZIP on first use or after cache expiry.
    Task<IReadOnlyDictionary<string, RouteInfo>> GetRoutesAsync(
        string mode, CancellationToken cancellationToken = default);
}
