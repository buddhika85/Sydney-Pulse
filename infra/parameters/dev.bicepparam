// dev.bicepparam
// --------------
// Parameter values for the dev environment (sydney-pulse-rg-dev).
// Auto-deployed from main branch via GitHub Actions (SP1-12).
//
// IMPORTANT: The existingServiceBusNamespaceName and
// existingServiceBusResourceGroup values below are real infrastructure
// names from the shared DevPulse subscription. Per ADR-0003, they live
// only in this parameter file — never in module code or docs.

using '../main.bicep'

// ── Environment ───────────────────────────────────────────────────────────────

param environment = 'dev'
param location    = 'australiaeast'

// ── Compute ───────────────────────────────────────────────────────────────────

// Y1 = Consumption plan — scales to zero between 30-second polls.
param functionAppSku = 'Y1'

// ── Frontend ──────────────────────────────────────────────────────────────────

// Free_F1: 20 concurrent connections, 20k messages/day (ADR-0008).
param signalRSku = 'Free_F1'

// ── Observability ─────────────────────────────────────────────────────────────

// 1 GB/day cap keeps App Insights within the free allowance.
// Sampling rate is set in host.json (APPLICATIONINSIGHTS_SAMPLING_PERCENTAGE = 5).
param appInsightsDailyCapGb = 1

// ── Shared Service Bus namespace (ADR-0003) ───────────────────────────────────
// Pre-existing Standard namespace shared with other DevPulse workloads.
// Only new topics/subscriptions are added — namespace config is never modified.

param existingServiceBusNamespaceName = 'devpulse-service-bus'
param existingServiceBusResourceGroup = 'DevPulseRG'
