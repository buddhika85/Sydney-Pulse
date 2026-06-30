# ADR-0013 — Trust the SignalR stream on the live dashboard (no periodic refetch)

**Date:** 2026-06-30
**Status:** Accepted

## Context

SP1-10 builds the live dashboard. The component keeps a client-side
`Map<vehicleId, Vehicle>` rendered as Leaflet markers. The Map is seeded
once from `GET /api/vehicles` at mount, then mutated by SignalR
`vehicleUpdated` messages for the lifetime of the session.

Two properties of the stack create a "ghost marker" risk:

- **SignalR is fire-and-forget.** `withAutomaticReconnect()` re-establishes
  the connection but does not replay messages emitted during the gap.
- **Cosmos `vehicles` container TTL is 5 minutes** (ADR-0002, ADR-0011).
  A vehicle that stops reporting (off-shift, GPS dropout) has its document
  expire server-side without emitting any "I'm gone" event the client can act
  on.

The combined effect: the in-memory client Map grows monotonically across a
long session. Markers for vehicles that no longer exist persist indefinitely.

Three options were evaluated for keeping the client Map consistent with
upstream truth:

1. **Periodic refetch** — re-call `GET /api/vehicles` every 60 s, replace the
   whole Map with the backend's current set. Self-heals dropped messages
   *and* removes ghost markers.
2. **Client-side TTL prune** — drop any marker whose `vehicle.timestamp`
   (the GTFS-RT-reported time, present on every payload) is older than the
   server-side TTL of 5 minutes. Checked on a local `setInterval`.
3. **Do nothing** — accept that ghost markers accumulate over the session
   length.

## Decision

**Option 2 — client-side TTL prune.** A 30-second `setInterval` removes any
marker whose `vehicle.timestamp` is older than 5 minutes. No periodic
refetch of `GET /api/vehicles`. The initial snapshot fetch at component
mount is retained — it is a one-shot, not a recurring poll.

## Reasoning

**The event stream is already the architectural source of truth.** ADR-0001
declared the system event-driven via Event Grid; the Poller publishes a
`VehicleUpdate.v1` event every 30 s for every active vehicle. Periodic
refetch from the HTTP API is a hedge against SignalR doing its job — and
the kind of hedge that quietly grows into the system's load-bearing path
once someone starts relying on it. Removing the refetch is a posture
statement: when SignalR is healthy, we trust it; when it isn't, we don't
paper over it.

**Dropped messages for active vehicles self-heal on next tick.** The Poller
re-publishes every 30 s. A message lost mid-transit for an active vehicle is
superseded by the next tick within the same 30-second window. The real
failure mode is silent vehicles — and silent vehicles are exactly what
client-side TTL handles directly.

**`vehicle.timestamp` carries stronger semantics than wall-clock-of-refresh.**
The GTFS-RT timestamp is the time TfNSW reported the vehicle's position.
Pruning by that field reflects actual upstream truth ("this position is
five minutes old"), not local optimism ("we asked again sixty seconds ago").
The client TTL threshold is aligned to the Cosmos container TTL on purpose
— they are the same constraint expressed at two layers.

**Cost is zero.** Option 1 would add roughly one HTTP round-trip per minute
per connected client, with downstream Cosmos read RUs. The numbers are tiny
at portfolio scale, but the right number for a hedge that exists to cover
a working primary path is zero.

**The HTTP API stays available for non-dashboard consumers.** Dropping
periodic refetch from the dashboard does not change the `VehiclesFunction`
server-side filter contract (`?mode=`, `?routeShortName=`). API integrations,
future mobile clients, and the SP1-08 contract tests continue to exercise
that path.

## Consequences

- `LiveComponent` owns a `setInterval(pruneStale, 30_000)` started in
  `ngAfterViewInit` and cleared in `ngOnDestroy`. Prune predicate:
  `Date.now() - new Date(vehicle.timestamp).getTime() > 5 * 60_000`.
- `VehiclesService.getVehicles()` is called **once** at mount for the
  initial snapshot. It is not called again.
- Following a SignalR disconnect longer than 5 minutes during which a
  vehicle kept running, the marker is pruned and does not return until
  the next message after reconnect. Acceptable failure mode — rarer than
  the ghost-marker problem this ADR fixes.
- The 5-minute prune threshold is coupled to the Cosmos container TTL.
  Any change to the container TTL (ADR-0002) must update the prune
  threshold here in lockstep. Drift creates either flicker (prune
  threshold shorter than TTL) or ghosts (prune threshold longer).
- This ADR explicitly does **not** apply to the SP4 ops view or future
  analytics surfaces. Those operate on Data Lake archive (ADR-0012) and
  have different freshness semantics.
