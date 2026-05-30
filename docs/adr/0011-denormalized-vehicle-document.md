# ADR-0011 — Denormalize route metadata into VehicleDocument

**Date:** 2026-05-31  
**Status:** Accepted

## Context

`VehicleDocument` is the Cosmos DB document written by `StateWriterFunction`
for every incoming `VehicleUpdate.v1` event. When SP1-08 added `GET /api/vehicles`,
the HTTP API needed to return `mode`, `routeLongName`, and `routeColor` alongside
each vehicle's position.

Three options were evaluated:

1. **Denormalize** — store `mode`, `routeLongName`, `routeColor` directly in
   `VehicleDocument` at write time (populated from the event, which already
   carries these fields from the Poller's GTFS static enrichment).
2. **Join at query time** — `VehiclesFunction` fetches route metadata from
   `TfNswFeedClient` and merges it with Cosmos results on every HTTP request.
3. **Omit** — return only the fields currently in `VehicleDocument`; accept
   that the response drifts from the `docs/api.md` contract.

## Decision

**Option 1 — denormalize.** Store `mode`, `routeLongName`, `routeColor`, and
`occupancyStatus` in `VehicleDocument` at write time.

## Reasoning

**Denormalization is the NoSQL default.** Cosmos is not a relational store.
The textbook pattern is to keep everything a typical query needs in one document
so reads are single-document lookups. Option 2 is precisely the join-at-query-time
anti-pattern Cosmos was designed to escape, and it would couple `VehiclesFunction`
to `TfNswFeedClient` — polling infrastructure — purely for read-time enrichment.

**Staleness risk is negligible.** `VehicleDocument` TTL is 5 minutes
(set at the Cosmos container level). Every existing document refreshes within
one poll cycle as the Poller writes new positions. Route metadata changes
happen quarterly at most. The classic "denormalization causes stale reads"
objection does not apply when the dataset has aggressive natural refresh.

**Cost of the change is minimal.** Four extra fields × ~50 bytes × ~3 000
documents = roughly 600 KB of additional storage. At Cosmos Serverless pricing
this is well under one cent per month.

**The Poller is already the single enrichment point.** `VehicleUpdate.v1`
events carry `routeLongName`, `routeColor`, and `mode` because the Poller
enriches them from the GTFS static cache before publishing to Event Grid.
`StateWriterFunction` receives the fully-enriched event; storing those fields
in `VehicleDocument` requires only mapping them through — no new dependencies,
no new logic.

**Archiver benefit.** Because enrichment happens before the Event Grid publish,
the Archiver Function captures fully-enriched events into Data Lake. Any future
consumer of the historical record gets route metadata without needing a separate
lookup.

## Consequences

- `VehicleDocument` gains `Mode`, `RouteLongName`, `RouteColor`, `OccupancyStatus`.
- `StateWriterFunction` maps four additional fields from `update` to `doc` — no
  new dependencies or logic required.
- `VehiclesFunction` reads a single Cosmos document per vehicle; no cross-service
  join at query time.
- Fields `status`, `stopName`, and `carriages` from the `docs/api.md` response
  schema are **deferred** — they require GTFS-RT trip update data not currently
  fetched by the Poller. They will be addressed in a future sprint when trip
  update decoding is added.
