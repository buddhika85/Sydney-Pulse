# ADR-0006: Deployment slots for blue/green, not feature flags

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

Sprint 2 introduces a production deployment strategy with the ability to
deploy a new version, validate it, and roll back if necessary. Two
common patterns are available:

1. Azure Function App deployment slots — deploy to a `staging` slot,
   run smoke tests, swap with `production`.
2. Feature flags — deploy code continuously to a single slot; control
   which features are active via Azure App Configuration toggles.

## Decision

Use Function App deployment slots for blue/green deployment. The
`production` slot is the live target; the `staging` slot receives new
deploys, gets smoke-tested by the CI pipeline, and is then swapped with
`production`.

Feature flags via Azure App Configuration are reserved for in-flight
behavioral toggles such as `EnableRippleDetection` or
`PollingIntervalSeconds`. They are not used for deployment-level safety.

## Consequences

Positive:

- Slot swap is atomic and near-instantaneous (DNS-level swap). A failed
  smoke test on the staging slot blocks the swap, so production is never
  exposed to a broken build.
- Rollback is one Azure CLI command (`az functionapp deployment slot
  swap`). No code changes, no redeploys.
- App settings can be slot-specific (Cosmos connection string can differ
  between slots), enabling true staging-environment behavior on the same
  Function App.
- Strong AZ-400 exam alignment — deployment slots are an explicit exam
  objective for "design and implement a deployment strategy."

Negative:

- Slot swap reuses the same compute plan, so a CPU-bound deployment can
  briefly affect production during swap. Acceptable for Consumption
  plan workloads that scale per-invocation.
- Function App slots are not available on the Consumption plan with all
  features — specifically, slot-specific app settings work, but custom
  domain bindings do not. Acceptable since we use the default
  `*.azurewebsites.net` URL behind Static Web Apps' proxy.
- Slots cost a small amount of extra storage. Negligible.

## Why not feature flags as the primary mechanism

Feature flags shine for *partial* rollouts, A/B testing, or kill-switches
for misbehaving features. They are weak as a deployment safety net
because:

1. Bad code can ship into production with a flag off, then be activated
   by a config change with no compile-time validation. Slot swaps make
   the *binary* the unit of release, which is safer.
2. Feature flag sprawl is a known operational problem. Reserve them
   for cases where they genuinely add value.

For Sydney Pulse, feature flags are appropriate for:

- `Mode` setting (live, demo, offline — see `/docs/modes.md`)
- Tunable parameters (polling interval, App Insights sampling rate)
- Toggles for experimental algorithms (ripple-detection v2, when written)

## Alternatives considered

**Two separate Function Apps for blue/green.** Rejected — doubles the
infrastructure cost and complicates configuration. Slots are the
purpose-built feature for exactly this need.

**Container Apps with revision-based traffic splitting.** Considered.
Provides smoother percentage-based rollouts than slot swap (10% → 50%
→ 100%). Rejected because moving from Functions Consumption to Container
Apps would add $30+/month in costs and rewrite the function host. Worth
revisiting if the project ever scales beyond portfolio.

## Related decisions

- ADR-0004 — Slots declared in `infra/modules/compute.bicep`
- ADR-0001 — Event-driven backend means slot swaps don't lose in-flight
  events; Event Grid retries any failed deliveries during the swap.
