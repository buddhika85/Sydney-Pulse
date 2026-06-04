# ADR-0012 — Archive as ML feature store (Parquet on Data Lake Gen2)

**Date:** 2026-06-04  
**Status:** Accepted

## Context

The Sydney Pulse pipeline produces a continuous stream of `VehicleUpdate.v1`
and `ServiceAlert.v1` events. Cosmos DB is configured as a hot live store
only — 5-minute TTL on `vehicles`, 24-hour TTL on `alerts` — which makes it
useless for any analytics workload.

Sprint 5 plans two analytics-driven features:

1. **KQL anomaly detection** — detect unusual disruption patterns by
   comparing live events against historical baselines.
2. **ONNX-in-Function predictor** — short-horizon delay prediction for a
   route, using vehicle position history as input features.

Both require a queryable historical record of every event the pipeline has
ever seen. SP1-15 implements the Archiver Function. The shape of the archive
is a forward-binding decision — once Sprint 5 starts reading these files,
re-shaping the schema costs a re-archive of the entire history. Locking in
the design now is cheaper than discovering it wrong later.

Constraints to honour:

- Queryable by Spark / Synapse / Azure ML / Pandas / DuckDB without bespoke
  parsing.
- Cheap at rest — long-term cold storage acceptable.
- Schema-stable across event-record versions (`.v1`, future `.v2`).
- Crash-safe under Event Grid's at-least-once delivery.
- Reasonable to implement in a portfolio sprint (no heavy operational
  burden).

## Decision

Archive every event to **Azure Data Lake Storage Gen2 as Apache Parquet
files**, with the following shape:

1. **Hive-partitioned layout** —
   `archive/yyyy=YYYY/MM=MM/dd=DD/HH=HH/events-{timestamp}.parquet`.
2. **Unified flat schema** — one Parquet schema covering both event types,
   discriminated by `eventType` and `eventVersion` columns. Type-specific
   fields nullable per row.
3. **Three timestamps per row** — `sourceTimestamp` (the event's
   source-observation moment), `publishedAt` (Event Grid receipt),
   `archivedAt` (Function write). The spec draft used the name
   `vehicleTimestamp` here, but the unified schema serves alerts too —
   `sourceTimestamp` reads honestly across both event types. Per event
   type it maps to: `VehicleUpdate.v1` → source `VehicleTimestamp`;
   `ServiceAlert.v1` → `StartsAt` (or `publishedAt` if `StartsAt` is null).
4. **Crash safety via append-blob staging + idempotent flush** — not
   Durable Functions Entities.
5. **`_manifest.json` per partition hour** — file list, event count, byte
   size; written by the Flush Function after Parquet write completes.
6. **Lifecycle policy** — Hot 0–30 days, Cool 30–90 days, Cold 90+ days,
   declared in Bicep at the storage-account level.

## Reasoning

**Parquet is the Lakehouse default.** Columnar layout means analytics
queries read only the columns they need, which is exactly the pattern KQL
and ONNX feature extraction will hit. Compression is typically 10–20× over
equivalent JSON. Schema is embedded in the file footer — self-describing
for any future reader. Native support in every tool we'd reach for.

**Hive partitioning enables predicate pushdown.** Synapse and Spark scan
only the hour-partitions matching a query's time filter. Hour granularity
matches our event rate (~10K–100K events/hour at peak) — that produces
Parquet files in the 1–10 MB range, which is the recommended sweet spot
(too small → metadata overhead; too large → poor parallelism).

**Unified schema beats per-event-type files for our use case.** ML feature
engineering routinely needs vehicle position joined with active alerts on
the same route at the same time. With one schema, the join is a single
predicate (`eventType = 'VehicleUpdate.v1'` vs `eventType = 'ServiceAlert.v1'`).
With separate files, every join requires cross-path reads and union logic.
Trade-off — rows have many nullable columns (alert fields null on vehicle
rows). Parquet compresses nulls efficiently; storage cost is negligible.

**Three timestamps unlock multiple feature classes.** ML feature
engineering uses each one differently:

- `sourceTimestamp` answers "when did this happen in the world?" — used
  for time-of-day features, delay prediction labels, weather correlation.
- `publishedAt` answers "when did our pipeline see it?" — used for
  feed-to-ingest latency features and TfNSW health monitoring.
- `archivedAt` answers "when was this row available for analytics?" — used
  for stream-consistency checks and archive-lag monitoring.

Losing any one of them eliminates a useful feature class. Storage cost of
three `DateTimeOffset` columns vs one is ~24 bytes per row — irrelevant.

**Append-blob staging gives crash safety without orchestration complexity.**
The naive Durable Functions answer is one Durable Entity per hour-partition,
accumulating events and flushed by a Timer-triggered orchestrator. That
works, but adds a substantial surface area: orchestration replay semantics,
entity state lifecycle, sub-orchestration patterns, harder testing.

The append-blob alternative is structurally simpler:

- `ArchiverIngestFunction` (Event Grid trigger) appends each event as a JSON
  line to a pending blob keyed by the event's `vehicleTimestamp` partition.
  Append-blob writes are atomic — either the line lands or it doesn't.
- `ArchiverFlushFunction` (Timer every 5 minutes) lists pending blobs whose
  partition hour is now closed, reads them, writes Parquet, writes the
  manifest, deletes the pending blob.
- Crash mid-flush leaves the pending blob intact; the next tick retries
  cleanly. Crash mid-ingest loses at most one event (the unwritten append
  call), and Event Grid retries the delivery anyway.

Single-threaded entity bottleneck under peak ingest is also avoided —
multiple ingest invocations append to the same pending blob in parallel
without coordination.

**The hour-boundary subtlety.** Events arriving at 14:59:58 belong in the
`HH=14` partition even if the Function processes them at 15:00:02.
Partition key is derived from `vehicleTimestamp`, not from
`DateTimeOffset.UtcNow`. This means a pending blob for `HH=14` may
continue to receive late writes for some minutes after the hour ticks
over. The Flush Function therefore only treats `HH=14` as closeable
once a grace window has elapsed (configurable; default 10 minutes after
the hour).

**Manifest file makes "is this partition queryable?" a single read.**
Without a manifest, an analytics query must list the blob container —
slow and racy when ingest is concurrent. The presence of
`_manifest.json` in a partition means "all events for this hour have been
flushed; this partition is safe to query."

**Lifecycle policy is a one-time Bicep declaration.** Hot 0–30 days keeps
recent data on fast storage for live dashboards. Cool 30–90 days drops the
storage cost ~80% for the still-recent archive most analytics queries hit.
Cold 90+ days reduces cost another ~50% for the long tail that supports
backfills and quarterly trend analysis.

## Alternatives considered

**JSON line-delimited files (`.jsonl`).** Rejected. No schema enforcement,
no compression, ~10× the storage cost. Forces schema-on-read which defeats
the feature-store purpose — every consumer would re-implement parsing.

**Cosmos historical container.** Rejected. RU model is wrong for analytics
(full scans burn RUs unpredictably). Cost at archive scale (~9 GB/month
uncompressed) would be 10–100× the Parquet equivalent.

**Per-event-type Parquet files.** Rejected. Joins between vehicle position
and alerts at the same point in time become cross-path queries. Unified
schema is the modern Lakehouse pattern.

**Durable Functions Entities for batching.** Rejected. The append-blob
design above achieves equivalent crash safety with less code, less
orchestration, and no single-threaded entity bottleneck per partition.

**Apache Avro instead of Parquet.** Rejected. Avro is row-oriented (good
for streaming write, poor for analytical read). Our access pattern is
batch-write then analytical-read; Parquet's columnar layout wins.

**Iceberg or Delta Lake table format on top of Parquet.** Rejected for
v1. These add ACID transactions, schema evolution, and time travel —
genuinely useful at production scale but premature for portfolio scope.
A future ADR can promote the archive to Iceberg/Delta if Sprint 5 hits
limits.

## Consequences

- New code in `SydneyPulse.Core/Archive/` (`ArchiveEvent`,
  `IParquetArchiveWriter`, `ParquetArchiveWriter`, `HivePartitionPath`,
  `ArchiveManifest`).
- New Functions `ArchiverIngestFunction` and `ArchiverFlushFunction` in
  `AzFunctions/EventPipeline/`.
- New `Parquet.Net` dependency added to `SydneyPulse.Core`.
- New `pending` container declared in `infra/modules/data.bicep` alongside
  the existing `archive` container.
- New `Microsoft.Storage/storageAccounts/managementPolicies` resource in
  `data.bicep` for the lifecycle rules.
- New `archiver` Event Grid subscription declared in
  `infra/modules/messaging.bicep`, filtering both `VehicleUpdate.v1` and
  `ServiceAlert.v1` event types. This replaces the placeholder noted in
  SP1-08 handoff.
- New unit-test classes for the schema, writer, partition-path builder, and
  both Functions; `docs/testing.md` updated accordingly.
- Up to a 5-minute flush latency before events are queryable from the
  archive — acceptable for analytics; the live UI continues to read from
  Cosmos.
- `Storage Blob Data Contributor` role on the Function App MI is already
  granted (SP1-03 `role-assignments.bicep`); no new role assignment
  required.
- Sprint 5 work — KQL anomaly detection and ONNX predictor — will read
  directly from this archive shape. Any change to the shape after Sprint 5
  starts requires a re-archive of historical data, which is operationally
  expensive. This ADR is the commitment.
