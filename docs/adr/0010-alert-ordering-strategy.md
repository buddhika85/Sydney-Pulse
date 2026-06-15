# ADR-0010: Alert delivery is per-route best-effort, not globally ordered

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

Service Bus Standard tier supports **message sessions**, which guarantee
FIFO ordering for messages sharing a session ID. The pre-existing
namespace reused for Sydney Pulse (ADR-0003) has this capability
available at no marginal cost.

The question: should we enable sessions on the `sydney-pulse-alerts`
topic subscription, using `routeShortName` as the session ID, to ensure
alerts for the same route arrive at the Alerter Function in publication
order?

## Decision

**Do not use sessions.** Alerts are delivered as independent messages
with at-least-once semantics and no enforced ordering. The frontend is
responsible for deduplication and conflict resolution using the alert's
`updatedAt` timestamp.

## Reasoning

Three observations drive this decision:

1. **Alerts are independent across routes.** A T1 alert and a 333 alert
   have no relationship. Strict global ordering across all alerts
   provides no user value.

2. **Within a single route, alert volume is low and update frequency
   is sparse.** A given route may publish 5–10 alerts in a busy day.
   Sub-second ordering granularity between two alerts for the same
   route is essentially impossible to violate in practice — the
   chance of two near-simultaneous publishes for one route is vanishingly
   small.

3. **The frontend handles out-of-order arrival gracefully.** Each alert
   has an `alertId` (unique) and `updatedAt` (timestamp). If two
   updates for the same `alertId` arrive in reverse order, the
   frontend uses the higher `updatedAt` and discards the older. This
   is the same logic that handles SignalR client reconnection where
   buffered messages might replay.

## Consequences

Positive:

- Simpler Alerter Function. Without sessions, the function can scale
  to multiple concurrent instances and process messages in parallel.
  Sessions would force single-threaded processing per session, capping
  throughput.
- No coupling to a Service Bus-specific feature. The Alerter chain
  could be migrated to a different broker (or to direct Event Grid
  consumption) without code changes.
- Lower latency. Sessions involve broker-side coordination overhead.
  Without them, message delivery is faster.

Negative:

- A theoretical race condition exists: if two operators at TfNSW
  publish overlapping updates to the same `alertId` within one polling
  window, the older timestamp could land in Cosmos after the newer.
  Mitigated by frontend timestamp comparison; not an in-flight
  ordering problem.
- Requires the frontend (and any future alert consumers) to be aware
  of and handle out-of-order delivery. Documented in `/docs/api.md`.

## What we explicitly accept

Out-of-order arrival of independent alerts is acceptable. Users will
see alerts in roughly the order they were published, with occasional
inversions of seconds at most.

Lost messages are not acceptable. At-least-once delivery is preserved
by Service Bus's lock + complete pattern; dead-letter queue catches
permanently failing messages for runbook-driven recovery.

Duplicate messages are not acceptable to surface to the user but are
acceptable in the pipeline. The frontend deduplicates by `alertId`.

## Alternatives considered

**Use sessions keyed by `routeShortName`.** Rejected for the throughput
and complexity reasons above. The marginal benefit (slightly better
ordering guarantees for an unlikely race condition) does not justify
the cost.

**Use sessions keyed by `alertId`.** Rejected because each `alertId`
would form its own session of typically one or two messages — sessions
are designed for long-running streams of related messages, not for
unique IDs.

**Skip Service Bus entirely; consume directly from Event Grid.**
Rejected because Event Grid does not offer dead-letter queues or
delivery retry windows as long as Service Bus does. Alert processing
benefits from being able to retry for hours if the SignalR push fails.

## Related decisions

- ADR-0001 — Event Grid + Service Bus messaging architecture
- ADR-0003 — Reused existing Standard namespace
- `/docs/api.md` — Alert payload schema and deduplication contract
- [justify_sb_usage.md](../justify_sb_usage.md) — Quick-read companion
  to this ADR: why Service Bus is in the alert flow at all, and the
  three-edit upgrade path if ordering ever becomes a requirement

## Forward compatibility

If a future requirement demands strict ordering (for example, a
regulatory audit trail of alerts), sessions can be enabled retroactively
by:

1. Updating the Bicep subscription declaration with `requiresSession: true`
2. Adding `[ServiceBusTrigger(..., IsSessionsEnabled = true)]` to the
   Alerter Function
3. Setting `SessionId = routeShortName` on the Event Grid → Service Bus
   subscription's message transformation

This would be an ADR-0010 amendment, not a wholesale rewrite.
