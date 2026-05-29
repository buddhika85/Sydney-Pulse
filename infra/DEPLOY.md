# Sydney Pulse — Bicep Deployment Guide

Everything a developer needs to deploy, update, or troubleshoot the
Sydney Pulse infrastructure. Always run what-if before deploying.

---

## Prerequisites

| Tool | Min version | Check |
|------|-------------|-------|
| Azure CLI | 2.57+ | `az --version` |
| Bicep CLI (bundled with az) | 0.26+ | `az bicep version` |
| Logged-in account | — | `az account show` |
| Correct subscription set | DevPulse subscription | `az account set --subscription <id>` |

Your account needs the following roles to deploy:

- **Contributor** on `sydney-pulse-rg-dev` (creates all resources)
- **Contributor** on `DevPulseRG` (creates the Service Bus topic — shared namespace)
- **Role Based Access Control Administrator** on `sydney-pulse-rg-dev`
  (the role-assignments module assigns RBAC roles to the Function App's
  Managed Identity; without this your deploy will fail at that step)

To seed Key Vault secrets after deploy (Step 5), your account also needs:

- **Key Vault Secrets Officer** on `sydney-pulse-kv-dev`

The Bicep only grants KV roles to the Function App's Managed Identity — your
developer account has no standing access by default. Grant it once after the
first deploy:

```powershell
az role assignment create --role "Key Vault Secrets Officer" --assignee <your-object-id> --scope "/subscriptions/<sub-id>/resourceGroups/sydney-pulse-rg-dev/providers/Microsoft.KeyVault/vaults/sydney-pulse-kv-dev"
```

Get your object ID with: `az ad signed-in-user show --query id --output tsv`

---

## File structure

```
infra/
  main.bicep                    Entry point. Orchestrates all modules.
  modules/
    security.bicep              Key Vault (Standard, RBAC-only, soft-delete 7 days)
    observability.bicep         Log Analytics workspace + App Insights (1 GB/day cap)
    data.bicep                  Cosmos DB Serverless, Functions storage, Data Lake Gen2
    messaging.bicep             Event Grid custom topic + 3 subscriptions
    servicebus-topic.bicep      Service Bus topic + subscription on shared namespace
    compute.bicep               Consumption plan + Function App (identity-based storage)
    frontend.bicep              SignalR Free_F1 + Static Web App (Free tier)
    role-assignments.bicep      RBAC grants for the Function App Managed Identity
  parameters/
    dev.bicepparam              Dev environment values (auto-deploy from main branch)
    prod.bicepparam             Prod environment values (manual approval required)
```

### What each module provisions

**`security.bicep`**
Key Vault `sydney-pulse-kv-dev`. RBAC-only (no legacy access policies).
Soft-delete enabled with 7-day retention. Secrets are never in app settings
or parameter files — the Function App resolves them at startup via
`@Microsoft.KeyVault(VaultName=...;SecretName=...)` references.

**`observability.bicep`**
Log Analytics workspace `sydney-pulse-law-dev` (PerGB2018, 30-day retention)
and App Insights `sydney-pulse-ai-dev` (workspace-based). Daily ingestion cap
is 1 GB — if the cap is hit, ingestion stops rather than charging overages.
Sampling is NOT set here; it is controlled via `host.json` at 5%.

**`data.bicep`**
Cosmos DB Serverless account `sydney-pulse-cosmos-dev` in `australiaeast`.
Contains two containers:
- `vehicles` — partition key `/routeShortName`, TTL 5 minutes (live state only)
- `alerts` — partition key `/routeShortName`, TTL 24 hours
Two storage accounts (alphanumeric names, no hyphens — Azure requirement):
- `sydpulsestordev` — Functions host internal state (triggers, leases)
- `sydpulsedlsadev` — Data Lake Gen2 for the Archiver (historical Parquet files)

**`messaging.bicep`**
Event Grid custom topic `sydney-pulse-eg-dev` (CloudEvents 1.0 schema) with
three subscriptions:
- `state-writer` — filters `VehicleUpdate.v1`, webhook destination (placeholder
  URL updated after Function App is deployed)
- `alerter` — filters `ServiceAlert.v1`, destination is the Service Bus topic
  (wired by computed resource ID — no placeholder needed)
- `archiver` — all event types, webhook destination (placeholder URL)

**`servicebus-topic.bicep`**
Deployed to `DevPulseRG` (not `sydney-pulse-rg-dev`). Adds topic
`sydney-pulse-alerts` and subscription `alerter-sub` to the pre-existing
`devpulse-service-bus` namespace. Namespace-level config is never modified
(ADR-0003 — the namespace is shared with other workloads).

**`compute.bicep`**
Consumption plan `sydney-pulse-func-dev-plan` (Y1, Windows) and Function App
`sydney-pulse-func-dev`. System-assigned Managed Identity enabled. Storage
access is identity-based (no connection string in settings). Three secrets are
pulled from Key Vault at startup — these must be seeded before the app runs
(see "Seeding Key Vault secrets" below).

**`frontend.bicep`**
SignalR Service `sydney-pulse-signalr-dev` (Free_F1, Serverless mode) and
Static Web App `sydney-pulse-swa-dev` (Free tier). The SWA uses `eastasia` as
its management plane region because `Microsoft.Web/staticSites` is not available
in `australiaeast`. Content is served globally via Azure CDN regardless of region.

**`role-assignments.bicep`**
Grants the Function App's Managed Identity the following roles:
- Key Vault Secrets User (read secrets)
- Cosmos DB Built-in Data Contributor (read/write documents)
- EventGrid Data Sender (publish events)
- Storage Blob Data Contributor on Data Lake (write Parquet files)
- Storage Blob Data Owner + Queue Contributor + Table Contributor on func storage
  (required for identity-based `AzureWebJobsStorage`)

---

## Step 1 — What-if (always run this first)

What-if is read-only. It shows exactly what Azure will create, modify, or
delete without touching anything. Review the output before deploying.

```powershell
az deployment group what-if --resource-group sydney-pulse-rg-dev --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam --no-pretty-print > what-if.json; Get-Content what-if.json
```

### Expected output

```
Resource changes: 17 to create, 1 to modify, 7 unsupported.
```

The `+` (create) list should contain all resources listed in the module
descriptions above. The `~` (modify) should be only SignalR (already exists
from the SP1-02 spike — Bicep brings it under management, no functional change).

Note: the `state-writer` and `archiver` Event Grid subscriptions are NOT
in this deployment. Event Grid validates webhook endpoints at subscription
creation time — a placeholder URL causes a 404 handshake failure and aborts
the whole deploy. These subscriptions are created via az CLI in Step 6 once
the real Function App URL is known.

Note: the App Insights daily cap (`currentbillingfeatures`) and proactive
detection settings (`ProactiveDiagnosticSettings`) are NOT in this deployment
either — those ARM APIs return 404 BadRequest on this subscription. The daily
cap is set manually via az CLI in Step 4.

### The 7 "unsupported" diagnostics

All 7 are RBAC role assignments. They use `reference()` to read the Function
App's Managed Identity principal ID, which doesn't exist until the Function App
is deployed. This is the standard pattern — these role assignments deploy
correctly at runtime. You can safely ignore these in what-if output.

### Known warnings (safe to ignore)

No warnings expected after the above exclusions. If any new BCP081 warnings
appear, check which resource they point to before deploying.

### Red flags to investigate before deploying

- Any `~` (modify) or `-` (delete) on resources outside `sydney-pulse-rg-dev`
  or `DevPulseRG` — something is misconfigured
- Any resource in the `~` list that is NOT `sydney-pulse-signalr-dev`
- A `+` for a new Service Bus **namespace** (not topic) — a missing `existing`
  keyword would create a new Standard namespace at ~$10/month

---

## Step 2 — Deploy

After reviewing the what-if output:

```powershell
az deployment group create --resource-group sydney-pulse-rg-dev --template-file infra/main.bicep --parameters infra/parameters/dev.bicepparam > deploy-out.txt 2>&1; Get-Content deploy-out.txt
```

Capturing to a file is recommended — error details are often truncated in the terminal.

**Expected duration:** 5–10 minutes (Cosmos DB account creation is the
longest step at ~3–5 minutes).

**Expected result:** JSON ending with `"provisioningState": "Succeeded"`.

If the deploy fails mid-way, it is safe to re-run the same command — Bicep
deployments are idempotent.

---

## Step 3 — Verify the deployment

Run these read-only checks after the deploy completes.

**All resources created in dev RG:**
```powershell
az resource list --resource-group sydney-pulse-rg-dev --output table
```

**Function App is running:**
```powershell
az functionapp show --name sydney-pulse-func-dev --resource-group sydney-pulse-rg-dev --query "state" --output tsv
```
Expected: `Running`

**Key Vault exists and RBAC is enabled:**
```powershell
az keyvault show --name sydney-pulse-kv-dev --resource-group sydney-pulse-rg-dev --query "properties.enableRbacAuthorization" --output tsv
```
Expected: `true`

**Cosmos DB Serverless is in the right region:**
```powershell
az cosmosdb show --name sydney-pulse-cosmos-dev --resource-group sydney-pulse-rg-dev --query "[location, capabilities[0].name]" --output tsv
```
Expected: `Australia East` and `EnableServerless`

**Service Bus topic created in the shared namespace:**
```powershell
az servicebus topic show --namespace-name devpulse-service-bus --resource-group DevPulseRG --name sydney-pulse-alerts --query "name" --output tsv
```
Expected: `sydney-pulse-alerts`

---

## Step 4 — Set App Insights daily cap

The `currentbillingfeatures` ARM API (used to set the daily ingestion cap)
returns a 404 BadRequest on this subscription when deployed via Bicep/ARM.
Set the cap manually via az CLI after the App Insights resource is created:

```powershell
az monitor app-insights component billing update --app sydney-pulse-ai-dev --resource-group sydney-pulse-rg-dev --cap 1
```

Expected output includes `"dataVolumeCap": {"cap": 1.0, ...}`.

Verify:
```powershell
az monitor app-insights component billing show --app sydney-pulse-ai-dev --resource-group sydney-pulse-rg-dev --query "dataVolumeCap.cap"
```
Expected: `1.0`

---

## Step 5 — Seed Key Vault secrets

The Function App will not start correctly until these three secrets exist in
Key Vault. Run each command once after the first deploy.

First, ensure your developer account has the Key Vault Secrets Officer role
(see Prerequisites above). If the `az keyvault secret set` commands return
`Forbidden`, grant the role and wait ~1 minute for RBAC propagation:

```powershell
az role assignment create --role "Key Vault Secrets Officer" --assignee <your-object-id> --scope "/subscriptions/<sub-id>/resourceGroups/sydney-pulse-rg-dev/providers/Microsoft.KeyVault/vaults/sydney-pulse-kv-dev"
```

```powershell
az keyvault secret set --vault-name sydney-pulse-kv-dev --name TfNswApiKey --value "<your-tfnsw-api-key>"
```

```powershell
az keyvault secret set --vault-name sydney-pulse-kv-dev --name AzureSignalRConnectionString --value "<primary-connection-string-from-signalr>"
```

```powershell
az keyvault secret set --vault-name sydney-pulse-kv-dev --name ServiceBusConnectionString --value "<primary-connection-string-from-service-bus>"
```

Where to get each value:
- **TfNswApiKey** — TfNSW Open Data portal → My Account → API Keys
- **AzureSignalRConnectionString** — Azure Portal → `sydney-pulse-signalr-dev` → Keys → Primary Connection String
- **ServiceBusConnectionString** — Azure Portal → `devpulse-service-bus` → Shared Access Policies → RootManageSharedAccessKey → Primary Connection String

---

## Step 6 — Update Event Grid webhook endpoints (post Function App deploy)

Two Event Grid subscriptions (`state-writer`, `archiver`) have placeholder
webhook URLs. Update them once the Function App URL is known.

Get the Function App hostname:
```powershell
az functionapp show --name sydney-pulse-func-dev --resource-group sydney-pulse-rg-dev --query "defaultHostName" --output tsv
```

Update state-writer subscription:
```powershell
az eventgrid topic event-subscription update --name state-writer --source-resource-id "/subscriptions/<sub-id>/resourceGroups/sydney-pulse-rg-dev/providers/Microsoft.EventGrid/topics/sydney-pulse-eg-dev" --endpoint "https://<funcapp-hostname>/runtime/webhooks/eventgrid?functionName=StateWriter"
```

Update archiver subscription:
```powershell
az eventgrid topic event-subscription update --name archiver --source-resource-id "/subscriptions/<sub-id>/resourceGroups/sydney-pulse-rg-dev/providers/Microsoft.EventGrid/topics/sydney-pulse-eg-dev" --endpoint "https://<funcapp-hostname>/runtime/webhooks/eventgrid?functionName=Archiver"
```

---

## Redeploying after a Bicep change

The deployment is idempotent — re-run the same `az deployment group create`
command after any Bicep change. Always run what-if first and review the diff.

To promote a change from dev to prod, update `prod.bicepparam` if the
parameter differs, then run with `--parameters infra/parameters/prod.bicepparam`
against `sydney-pulse-rg-prod`. Prod deployments require manual approval in
GitHub Actions (SP1-12).

---

## Cost notes

| Resource | Dev cost | Notes |
|----------|----------|-------|
| Cosmos DB Serverless | ~$0.50–$2/month | Billed per RU — hot loops without throttling can spike to $20+/hour |
| Functions Consumption (Y1) | ~$0/month | Scales to zero between 30-second polls |
| SignalR Free_F1 | $0 | 20 concurrent connections, 20k messages/day cap |
| Static Web App Free | $0 | 100 GB bandwidth/month included |
| App Insights | ~$0/month | 1 GB/day cap; 5% sampling; first 5 GB/month free |
| Log Analytics | ~$0/month | 30-day retention; first 5 GB/month free |
| Key Vault Standard | ~$0.05/month | Billed per 10k operations |
| Storage (×2) LRS | ~$0.05/month | Dev usage is minimal |
