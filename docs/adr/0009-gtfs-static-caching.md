# ADR-0009: GTFS static feeds cached in memory for 1 hour

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

The `TfNswFeedClient` joins realtime vehicle position data with static
GTFS metadata to produce enriched payloads. Static metadata includes
the `route_short_name` (e.g. `T1`), `route_long_name`, `route_color`
(used for line-colored UI), and `stop_name` for stop IDs.

The static feeds are large (multi-MB ZIP files containing CSV files
like `routes.txt` and `stops.txt`) and rarely change — Sydney Trains
revises its schedule a few times a year at most. Fetching them on
every realtime poll would be wasteful and would burn TfNSW API quota.

## Decision

Cache parsed GTFS static data (route and stop lookups) in-memory
inside the `TfNswFeedClient` for **1 hour**. The cache is per Function
instance.

On the first call after a cold start or after a 1-hour expiry, the
client downloads the static ZIP, parses `routes.txt` and `stops.txt`,
and builds two `Dictionary<string, T>` lookups. Subsequent calls return
the cached lookups immediately.

```csharp
public class TfNswFeedClient {
    private readonly Dictionary<string, RouteLookup> _routeCacheByMode = new();
    private readonly Dictionary<string, DateTimeOffset> _cacheExpiry = new();
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);

    public async Task<RouteLookup> GetRoutesAsync(string mode) {
        if (_cacheExpiry.TryGetValue(mode, out var expiry) && expiry > DateTimeOffset.UtcNow) {
            return _routeCacheByMode[mode];
        }
        var lookup = await FetchAndParseStaticAsync(mode);
        _routeCacheByMode[mode] = lookup;
        _cacheExpiry[mode] = DateTimeOffset.UtcNow.Add(CacheTtl);
        return lookup;
    }
}
```

## Consequences

Positive:

- Static feed fetched at most once per hour per Function instance.
  Typical Consumption plan instances last 5–20 minutes between cold
  starts, so in practice the cache is rebuilt on every cold start with
  one download per mode.
- TfNSW API quota usage drops from "every 30 seconds" to "once per
  hour per instance". Negligible compared to the realtime polling.
- Cosmos DB schema stays simple — no route or stop tables. Static
  metadata is derived at runtime from the cache.

Negative:

- Up to 1 hour of staleness when TfNSW publishes a new schedule or
  renames a stop. Acceptable for portfolio scale; transit timetable
  changes are scheduled events with weeks of notice.
- Per-instance cache means multiple Function instances duplicate the
  download. At the rate of Consumption plan scaling for this workload
  (typically 1–2 instances), the duplication is minimal.
- Cold-start latency for the first poll is higher (must download +
  parse the static ZIP). Acceptable because the Poller is timer-
  triggered, not user-facing.

## Why not Redis or Cosmos for the cache

**Redis (Azure Cache for Redis).** Rejected because Basic tier starts at
~$20/month. Wipes out the entire cost saving from Cosmos Serverless.
The in-memory cache hits 99%+ of the same benefit at zero cost.

**Cosmos DB cached collection.** Rejected because it adds RU costs and
operational complexity (cache invalidation logic, separate container)
for negligible benefit. In-memory is simpler.

**Azure App Configuration.** Considered as a centralised cache. Rejected
because reading thousands of route entries from App Configuration would
itself be slow and rate-limited.

## Cache key and invalidation

- Cache key: transport mode (`trains`, `buses`, `ferries`, etc.)
- TTL: 1 hour, sliding window not used (deliberate fixed expiry)
- Invalidation: time-based only. No manual invalidation API exposed.

If a critical timetable change is published mid-day and an out-of-band
cache flush is needed, the operational procedure is "restart the
Function App" — slot swap with itself works for this.

## Related decisions

- ADR-0002 — Cosmos schema is simple precisely because route metadata
  lives in this cache, not in a database table.
- `/docs/architecture.md` — TfNswFeedClient responsibilities

## Implementation note

`TfNswFeedClient` is registered as a singleton in the Functions DI
container. This ensures the in-memory cache is shared across function
invocations within the same instance. Without singleton lifetime, each
invocation would build its own cache (defeating the purpose).
