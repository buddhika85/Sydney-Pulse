# ADR-0003: Reuse existing Service Bus Standard namespace

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

The Alerter chain (described in ADR-0001) needs a queue or topic to
carry `ServiceAlert.v1` events from Event Grid through to the Alerter
Function with at-least-once delivery, dead-letter handling, and the
option of session-based ordering.

An existing Service Bus Standard namespace exists in the same subscription
from prior work. It already serves other workloads.

Three options were viable:

1. Create a new Basic-tier namespace dedicated to Sydney Pulse (~$0.50/mo).
2. Create a new Standard-tier namespace dedicated to Sydney Pulse
   (~$10/mo base).
3. Reuse the existing Standard namespace, adding a new topic.

## Decision

Reuse the existing Service Bus Standard namespace. Add a new topic
named `sydney-pulse-alerts` with one subscription `alerter-sub`. The
topic is declared in Bicep via the `existing` keyword on the namespace
plus a new resource for the topic.

```bicep
resource serviceBusNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: serviceBusNamespaceName
  scope: resourceGroup(serviceBusResourceGroup)
}

resource alertsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: serviceBusNamespace
  name: 'sydney-pulse-alerts'
  properties: { ... }
}
```

## Consequences

Positive:

- Marginal monthly cost is zero — the namespace is already paid for.
- Standard tier features (topics, subscription filters, sessions, larger
  message size) available at no additional cost. This is upside compared
  to the Basic-tier-on-its-own scenario.
- Lower operational surface area — one namespace to manage, not two.

Negative:

- Shared resource with other workloads creates noisy-neighbour risk.
  Mitigated by Standard tier's per-namespace messaging unit guarantees
  and by the modest message volume (~1 alert/min, well within the
  free-of-charge envelope).
- Bicep needs to reference an `existing` resource in a different
  resource group, which complicates parameter files. Acceptable
  one-time setup cost.
- If Sydney Pulse ever needs to scale to a dedicated namespace later,
  topic and subscription names will need migration. The topic naming
  prefix `sydney-pulse-*` makes the boundary clear if that day comes.

## Alternatives considered

**New Basic namespace.** Rejected because Basic doesn't support topics —
only queues. Subscription filters at Event Grid plus a queue would work
functionally, but we lose the option of multiple subscriptions on the
same topic for future scenarios (a "notify operator on critical alerts"
subscription, for example).

**New Standard namespace.** Rejected because there is no functional
benefit over the reused namespace, and $10/month is real money on a
portfolio budget.

## Related decisions

- ADR-0001 — Event Grid + Service Bus messaging architecture
- ADR-0010 — Alert ordering chosen as per-route best-effort. Sessions
  are available on this Standard namespace if we ever change our mind.

## Operational note

When updating Bicep that touches the shared namespace, always verify
the `what-if` output does not modify namespace-level properties (SKU,
zone redundancy, capacity, network ACLs). Only topic and subscription
resources should appear in changes for Sydney Pulse deployments.
