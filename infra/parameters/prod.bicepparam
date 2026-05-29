// prod.bicepparam
// ---------------
// Parameter values for the prod environment (sydney-pulse-rg-prod).
// Deployments to prod require manual approval before slot swap (ADR-0006).
// Never deploy directly from a developer machine — use GitHub Actions only.
//
// At portfolio scale, prod and dev share the same SKUs. The separation
// exists for operational isolation (separate RGs, separate KV, separate
// Cosmos account) not for capacity differences.

using '../main.bicep'

// ── Environment ───────────────────────────────────────────────────────────────

param environment = 'prod'
param location    = 'australiaeast'

// ── Compute ───────────────────────────────────────────────────────────────────

// Consumption plan is sufficient at portfolio scale. If sustained traffic
// warrants it, upgrade to EP1 (Elastic Premium) in Sprint 3+.
param functionAppSku = 'Y1'

// ── Frontend ──────────────────────────────────────────────────────────────────

// Free_F1 is the correct choice for prod at portfolio scale (ADR-0008).
// Load testing against the live dashboard is forbidden — 20-connection cap.
param signalRSku = 'Free_F1'

// ── Observability ─────────────────────────────────────────────────────────────

// Same cap as dev — prod traffic volume does not justify raising the cap.
// Sampling rate is set in host.json (APPLICATIONINSIGHTS_SAMPLING_PERCENTAGE = 5).
param appInsightsDailyCapGb = 1

// ── Shared Service Bus namespace (ADR-0003) ───────────────────────────────────
// Same shared namespace as dev — Standard tier supports multiple topics.
// Both environments add their own topics; they do not share topics.

param existingServiceBusNamespaceName = 'devpulse-service-bus'
param existingServiceBusResourceGroup = 'DevPulseRG'
