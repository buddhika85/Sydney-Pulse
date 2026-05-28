# ADR-0008: SignalR Service Free SKU is sufficient at portfolio scale

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

The Live dashboard needs server-push for vehicle position updates and
new service alerts. Azure SignalR Service is the chosen managed
WebSocket provider. It offers three tiers:

- Free — 20 concurrent connections, 20,000 messages/day, 1 unit max
- Standard — paid per unit, ~$48/month per unit, 1,000 connections/unit
- Premium — for very high scale, $670+/month

## Decision

Use the Free SKU. Plan for portfolio-scale traffic only (a handful of
concurrent demo visitors at most). Document the connection cap as a
deliberate constraint, not a limitation.

## Consequences

Positive:

- Zero monthly cost. $48/month saved versus Standard tier.
- All SignalR Service features are available on Free except connection
  count and per-day message throughput. Negotiate endpoint, group
  broadcasts, Function output binding — all work identically.
- No scaling decision to make until real traffic arrives.

Negative:

- Capped at 20 concurrent WebSocket connections. The 21st visitor
  gets a connection rejection.
- 20,000 messages/day cap. With ~5,000 vehicle updates broadcast every
  30 seconds (300 per minute, ~18,000 per hour), the broadcast volume
  per *connection* would blow the daily cap if we naively broadcast
  every update to every connection.

## How we stay under the limits

Two design choices keep usage well within the Free SKU envelope:

1. **Throttle broadcasts to ~1 per second per group.** The Poller publishes
   every 30 seconds, but the State Writer aggregates updates before
   broadcasting. The frontend doesn't need every individual update —
   it needs a fresh picture every second or so. This caps messages at
   ~86,400/day across all connections, well under 20,000 *per connection*.

2. **Use SignalR groups, not per-connection messaging.** All clients
   subscribe to the `vehicles` group. One broadcast goes to all
   connections at once; SignalR Service handles fan-out internally.

## Caveats and runbook implications

- If LinkedIn post about the project goes viral and 25 people open the
  live dashboard simultaneously, some will see "connection failed."
  Documented as acceptable in the project's stated portfolio scope.
- During development, multiple browser tabs to localhost count as
  multiple connections. Close stale tabs when debugging or use the
  HTTP API directly to inspect data.

## Alternatives considered

**Standard SKU 1 unit.** Rejected for portfolio scale. Would be the right
choice if real production traffic materialized.

**Self-hosted SignalR in the Function App.** Rejected because Functions
on Consumption plan don't support persistent WebSocket connections
(they hibernate between invocations). Moving to Premium plan to support
this would cost more than just paying for SignalR Standard.

**Server-Sent Events (SSE) instead of WebSockets.** Considered as a
free alternative. Rejected because Functions HTTP triggers have a
maximum execution time, making long-lived SSE connections unreliable.
SignalR Service was purpose-built for this case.

**Poll the HTTP API every 5 seconds from the browser.** Rejected because
it loses the "real-time" feel and produces much higher load on Cosmos
than push delivery.

## Upgrade trigger

Upgrade to Standard SKU 1 unit when any of:

- Concurrent connections approach 15 regularly (75% of cap)
- Daily message count exceeds 15,000 (75% of cap)
- Real production launch is planned

The upgrade is a Bicep parameter change — `signalRSku: 'Standard_S1'` —
followed by a `az deployment` apply. Zero code changes required.

## Related decisions

- ADR-0001 — SignalR receives pushes from the Alerter Function via
  output binding
- ADR-0007 — SignalR is the primary live channel for the commuter
  dashboard but not for the SRE dashboard (which polls /api/ops/*)
