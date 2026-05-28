# ADR-0001: Event-driven architecture with Event Grid as the router

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

Sydney Pulse polls TfNSW GTFS-realtime feeds every 30 seconds, processes
the resulting vehicle updates and service alerts, persists current state,
archives history, and pushes live updates to browsers.

Three options were viable for the messaging backbone between the Poller
and the downstream consumers:

1. The Poller writes directly to all downstream stores (Cosmos, Data Lake,
   SignalR) synchronously.
2. The Poller publishes to a Service Bus topic; consumers subscribe.
3. The Poller publishes to an Event Grid custom topic; consumers
   subscribe, with one subscription routing alerts on to a Service Bus
   topic for ordered processing.

## Decision

Use Event Grid as the primary fan-out, with a Service Bus topic
downstream of one Event Grid subscription to carry service alerts only.

The Poller publishes two event types: `VehicleUpdate.v1` and
`ServiceAlert.v1`. Three Event Grid subscriptions consume them:

- `state-writer` — `VehicleUpdate.v1` → State Writer Function → Cosmos DB
- `alerter` — `ServiceAlert.v1` → Service Bus topic → Alerter Function
  → SignalR
- `archiver` — both event types → Archiver Function → Data Lake

## Consequences

Positive:

- The Poller does not need to know about its consumers. Adding a new
  consumer (for example, a future Slack-notification function) is a
  Bicep change only.
- Event Grid handles retry and dead-lettering per subscription. A
  failing State Writer does not block the Archiver.
- Subscription filters at Event Grid level keep the Alerter chain from
  receiving traffic it doesn't need. The Service Bus topic only sees
  ~1 event per minute instead of ~5,000.
- The Alerter chain gets Service Bus features (sessions if needed,
  dead-letter queue, scheduled messages) without imposing them on the
  high-volume vehicle update path.

Negative:

- Two messaging services to operate instead of one. Slight increase in
  operational complexity. Mitigated by the fact that both are managed
  services with minimal config drift risk.
- An extra hop adds ~50 ms to alert delivery latency. Acceptable for
  alerts (user-perceived latency is dominated by SignalR delivery
  anyway).
- Slightly higher cost than pure Event Grid, but Service Bus is
  reused from an existing Standard namespace (ADR-0003) so marginal
  cost is zero.

## Alternatives considered

**Direct writes from Poller.** Rejected because the Poller would become
a fat orchestrator with knowledge of every downstream system. Any
downstream failure stalls the entire pipeline. No retry isolation.

**Service Bus topic only (no Event Grid).** Rejected because Service
Bus subscription filters use SQL-like expressions evaluated by the
broker on every message — fine for small fan-out, but charging the
vehicle-update flow through a broker is overkill when Event Grid's
push-based delivery is cheaper and simpler. The single feature Service
Bus has that Event Grid lacks (sessions) is only needed for alerts
and we get it on the alert sub-path anyway.

## Related decisions

- ADR-0003 — Reuses existing Service Bus Standard namespace
- ADR-0010 — Alert ordering is per-route best-effort, not strict
