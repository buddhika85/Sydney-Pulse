# Sprint — Multi-mode expansion (buses)

**Target:** 4–5 days
**Goal:** Expand Sydney Pulse from sydneytrains-only to full bus mode coverage,
respecting TfNSW's 5 RPS / 60 k/day rate limit and Sydney's per-operator
GTFS-RT feed structure.

**Scheduling:** Floating — slot in after Sprint 1 (MVP live URL with trains)
and Sprint 2 (CI/CD + multi-env). Not numbered into the main 1→5 roadmap
because the existing sprints have tight themes (Observability, Polish, ML)
that bus expansion would dilute.

**Origin:** Deferred from SP1-16 cloud smoke. See
[`backend-manual-deploy-plan.md`](backend-manual-deploy-plan.md) Phase D
and the bus story in `docs/sp1-16-debug-stories.md` for the discovery
narrative.

## Why this is a sprint, not a config flag

Three constraints make bus support non-trivial:

1. **No flat `/buses` URL.** TfNSW contracts buses to ~14 private operators
   (`SMBSC001`–`SMBSC014` — Sydney Metropolitan Bus Service Contracts).
   Each operator publishes a separate GTFS-RT feed. The realtime URL
   pattern is `/v2/gtfs/vehiclepos/buses/{operator}` — `mode` is no longer
   a flat string.
2. **Rate-limit math forces a cadence redesign.** TfNSW caps at
   **5 RPS, 60 k requests/day**. The current Poller (30 s ticks,
   2 endpoints per mode) sits at ~5,760 calls/day — massive headroom.
   Adding all 14 bus operators at 30 s makes it:

   | Polling cadence | Calls / minute | Calls / day | Within 5 RPS / 60k? |
   |---|---|---|---|
   | Trains only (current) | 4 | 5,760 | Yes (huge headroom) |
   | Trains + 14 bus operators every 30 s | 60 | 86,400 | **No — over quota** |
   | Trains every 30 s + buses every 2 min | ~10 | ~14,400 | Yes |
   | Trains every 30 s + buses every 5 min, staggered | ~7 | ~10,300 | Yes |

3. **Schema and partitioning side-effects.** Bus `route_short_name`s
   collide across operators (`400` exists on multiple SMBSCs). Affects:
   - Cosmos partition key (currently `routeShortName` — needs
     `{operator}|{routeShortName}` or equivalent)
   - GTFS static route cache (currently keyed by `routeId` per mode —
     needs to disambiguate per operator)
   - UI filters (bus route number alone isn't unique)

## Sprint Backlog

| # | Title | Description | Days |
|---|-------|-------------|------|
| 1 | Operator discovery | Hardcode the 14 SMBSC codes from TfNSW Open Data Hub. Optional stretch: fetch the live list dynamically at host startup. | 0.5 |
| 2 | Config schema redesign | `VehicleModes` becomes a hierarchical structure — e.g. `[{ mode: "sydneytrains" }, { mode: "buses", operators: ["SMBSC001", …] }]` — bound via `IOptions<TfNswOptions>`. | 0.5 |
| 3 | Polling cadence redesign + ADR | Per-mode timer cadence (trains 30 s, buses 2–5 min) — staggered start to spread load. New ADR-0013 locking in the rate-limit-driven cadence design. | 1 |
| 4 | GTFS static cache update | Per-operator route metadata so colour, long name, and short name resolve correctly under collisions. | 0.5 |
| 5 | Cosmos partition key migration | Disambiguate route ID by operator. Decision: change partition key on the `vehicles` container, or change `Id` to include operator? Either way, migration plan + ADR-0014. | 0.5 |
| 6 | Frontend mode filter + bus icons | Live dashboard adds bus toggle; route filter respects operator-disambiguated keys. | 0.75 |
| 7 | Tests | Unit tests for the new config schema and per-mode timer logic; integration tests against recorded TfNSW bus fixtures (use cassettes captured locally). | 1 |
| 8 | Smoke + observability | End-to-end smoke per operator. App Insights dashboard panel for bus dependency latency + 4xx rate per operator. | 0.5 |

**Total:** 4.25–5 days.

## Risks

- **TfNSW operator list churn.** Bus contracts rebid periodically — the
  SMBSC list isn't immutable. Dynamic discovery (item 1 stretch) mitigates
  but adds a startup dependency.
- **Cosmos partition key change is a breaking schema change.** The
  `vehicles` container has only ephemeral data (TTL 5 min), so a clean
  flush is acceptable — but the change locks in if we ship live data
  against it. Decide before SP1-16 trains data accumulates significant
  history.
- **Polling cadence interaction with Polly circuit breaker.** Each bus
  operator's HttpClient should use its own circuit-breaker instance,
  otherwise one failing operator opens the breaker for all operators
  (the same trap that hid the buses-only 404 during SP1-16 — see
  `sp1-16-debug-stories.md` story #7).

## Deliverables

- Live dashboard showing both trains and buses simultaneously
- Per-operator polling at quota-safe cadence
- ADR-0013 (polling cadence) and ADR-0014 (partition key)
- Updated cost-model.md reflecting bus polling math
- Updated architecture.md and api.md

## Acceptance Criteria

- Each of the ~14 SMBSC bus operators shows ≥1 invocation/day in App
  Insights `dependencies` with 2xx
- Total TfNSW calls/day stays under 60 k (verify with KQL summary)
- Cosmos `vehicles` queries return per-operator-disambiguated documents
- UI filter "Bus → 400" returns only the right operator's 400s
- Polly circuit breaker on one bus operator doesn't open the breaker
  for trains or other operators

## Out of scope

- Light Rail, Metro, Ferries — separate mode-expansion sprints
- Per-operator branding (operator logos, contractor names in UI) —
  Sprint 4 polish concern
