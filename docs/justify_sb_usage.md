# Why Service Bus sits between Event Grid and the Alerter Function

Quick-read interview prep. Captures the SP1-16 discussion on alert-pipeline
design. See [ADR-0001](adr/0001-event-driven-with-eventgrid.md) and
[ADR-0010](adr/0010-alert-ordering-strategy.md) for the long version.

## Bottom line

> SB sits between EG and the Alerter for **delivery guarantees** (DLQ,
> retry control, backpressure) — **not for ordering**. ADR-0010 explicitly
> rejected ordering. The Bicep is one property away from per-route FIFO
> if a future requirement demands it.

## Why SB is in the flow (the actual reasons)

| What SB gives us | Bicep location |
|---|---|
| **DLQ** — `maxDeliveryCount: 5`, bad messages quarantined for replay | `modules/servicebus-topic.bicep` line 45 |
| **Lock + complete** retry semantics — `lockDuration: PT1M` | line 46 |
| **TTL + dead-letter on expiry** — `defaultMessageTimeToLive: P1D` | lines 47–48 |
| **Backpressure buffer** — alerts queue if Alerter slow / Cosmos throttles | inherent |
| **Pull consumer** — no EG webhook validation handshake (story #5 pain) | inherent |
| **Replay surface** — Service Bus Explorer for peek / requeue from DLQ | inherent |
| **Subscription filter at EG layer** — only `ServiceAlert.v1` enters SB | `modules/messaging.bicep` line 99 |

Vehicles skip SB on purpose: high volume (~3500 ev / 30 s), drops tolerable
(next tick overwrites), low-latency direct EG webhook is faster.

## Why we don't order alerts (ADR-0010)

1. **Alerts across routes are independent** — T1 alert vs 333 alert: no
   relationship. Global order = no user value.
2. **Volume per route is low** — ~5–10 alerts/day/route. Sub-second
   ordering between two updates for the same route almost never happens.
3. **Frontend handles out-of-order arrival** — each alert has `alertId`
   (unique) + `updatedAt` (timestamp). Newer `updatedAt` wins; older
   discarded. Same pattern handles SignalR reconnection replays.

## How SB *would* guarantee ordering (if enabled)

**Critical**: SB does NOT order by timestamp. It orders by **arrival order
at the broker**, grouped by `SessionId`. Per-session FIFO, not global,
not timestamp-sorted.

Model: **group by `SessionId` → within each group, delivery order =
enqueue order**. If you want timestamp ordering, the consumer still has
to sort.

For Sydney Pulse the candidate key was **`SessionId = routeShortName`**
→ per-route FIFO. `SessionId = alertId` was rejected (sessions of 1–2
messages defeat the purpose).

## What it would take to switch on

Three edits — ADR-0010 lines 110–122 already documents this path:

1. **`modules/servicebus-topic.bicep`** — add `requiresSession: true` to
   the `alerter-sub` properties block.
2. **`modules/messaging.bicep`** — set a delivery property on the EG → SB
   subscription mapping `data.routeShortName` → SB `SessionId` header.
3. **`AlerterFunction.cs`** — `[ServiceBusTrigger(..., IsSessionsEnabled = true)]`.

Trade-off: sessions force **single-threaded processing per session**, so
throughput per route is capped. Acceptable for alerts (low volume) — not
acceptable for vehicles (would throttle the live dashboard).

## Interview soundbite

> *"Service Bus is in the alert flow for delivery guarantees, not
> ordering. The big three: dead-letter queue, retry control via
> max-delivery-count and lock+complete, and backpressure when Cosmos
> throttles. We deliberately skipped sessions — ADR-0010 — because alert
> volume per route is low and the frontend deduplicates by `alertId`
> plus `updatedAt`. Sessions are one Bicep property away if a future
> regulatory ordering requirement comes in."*

## Related

- [ADR-0001](adr/0001-event-driven-with-eventgrid.md) — Event Grid + SB
  messaging architecture
- [ADR-0003](adr/0003-service-bus-tier-choice.md) — Reused existing
  Standard namespace
- [ADR-0010](adr/0010-alert-ordering-strategy.md) — Full reasoning on
  no-ordering decision
- [sp1-16-debug-stories.md](sp1-16-debug-stories.md) story #5 — why pull
  consumer beats webhook for the Alerter path
