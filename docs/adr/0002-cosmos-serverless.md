# ADR-0002: Cosmos DB Serverless, not provisioned throughput

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

Sydney Pulse needs a NoSQL store for live vehicle state and active service
alerts. Read traffic is bursty (driven by demo visitors), write traffic is
steady (~14M writes/month from the State Writer Function). Cosmos DB is
already chosen as the data platform.

Two billing models are available:

1. Provisioned throughput — reserve N request units per second, billed
   continuously regardless of actual use.
2. Serverless — pay per request unit consumed, no minimum.

The Azure free tier (1000 RU/s + 25 GB free forever) is unavailable on
this subscription because it has already been used elsewhere — that
benefit is one-time per subscription, set at account creation.

## Decision

Use Cosmos DB Serverless for both `vehicles` and `alerts` containers.

Partition key: `/routeShortName` (e.g. `T1`, `333`, `M1`). This matches
the UI's primary grouping and gives reasonable distribution across the
~50 distinct route_short_name values across all modes.

Per-container TTL set on documents: 5 minutes for `vehicles`,
24 hours for `alerts`. Old documents are auto-purged by Cosmos with no
additional code or scheduled cleanup needed.

## Consequences

Positive:

- Idle hours cost zero RUs. A portfolio project that nobody is looking
  at right now pays nothing for reads.
- Estimated monthly cost: $3–8 versus $24/month minimum for 400 RU/s
  provisioned. Roughly 70% saving at portfolio scale.
- No capacity planning. The State Writer Function's bursty publish
  pattern (5,000 events every 30 seconds) is absorbed without
  hand-tuning RU/s.

Negative:

- No SLA on availability or latency below 99.9%. Provisioned offers
  99.99% with multi-region. Acceptable for portfolio scale.
- Cosmos Serverless has a 5,000 RU/s burst ceiling per partition. If
  one route gets a sudden spike (unlikely for transit data), it would
  throttle. Mitigated by partition key choice spreading load.
- Cannot switch to provisioned without a data migration. If real
  production traffic ever arrives, plan a one-day cutover.

## Alternatives considered

**Provisioned 400 RU/s autoscale.** Rejected — minimum spend of $24/month
even when idle. Autoscale lower bound is 10% of max, so 40 RU/s minimum,
still billed continuously. Not justifiable when idle is the steady state.

**Azure Table Storage.** Considered as a cheaper alternative ($0.05/GB).
Rejected because we want SQL-like querying for the analytics screen
and SDK ergonomics matter for developer velocity.

**PostgreSQL Flexible Server.** Rejected — $25/month minimum and we have
no relational requirements. Cosmos JSON documents match GTFS-realtime
payloads more naturally.

## Related decisions

- ADR-0009 — GTFS static feeds are cached in memory in `TfNswFeedClient`,
  not stored in Cosmos. Keeps the Cosmos schema simple.
