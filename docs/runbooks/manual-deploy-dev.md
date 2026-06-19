# Runbook: manual deploy to dev

How to deploy the SydneyPulse backend (infra + Function App code) to
`sydney-pulse-rg-dev` by hand. Reproducible recipe — produced by SP1-16,
will be automated by SP1-12 (GitHub Actions CI/CD).

## When to use this

| Scenario | Steps to run |
|---|---|
| First-ever deploy to a fresh RG | All steps 1 → 4 |
| Function App code change only | Step 2 + Step 4 smoke |
| Bicep change only (no code change) | Step 1 only (`infra/DEPLOY.md` Steps 1–3) |
| Secret rotation | `infra/DEPLOY.md` Step 5 only |
| Full restore after RG delete | All steps 1 → 4, secrets re-seeded |

Related: [`infra/DEPLOY.md`](../../infra/DEPLOY.md) (Bicep details),
[`dev-smoke-evidence.md`](dev-smoke-evidence.md) (full smoke pack with
KQL + screenshots), ADR-0003 (shared Service Bus), ADR-0008 (SignalR
Free SKU).

---

## Prerequisites

### Tools

| Tool | Min version | Verify |
|---|---|---|
| Azure CLI | 2.57+ | `az --version` |
| Bicep CLI (bundled) | 0.26+ | `az bicep version` |
| .NET SDK | 8.0.x | `dotnet --version` |
| Azure Functions Core Tools | 4.x | `func --version` |
| GitHub CLI | 2.x | `gh --version` (optional for this runbook; needed for SP1-12) |

### Azure RBAC

See [`infra/DEPLOY.md`](../../infra/DEPLOY.md) "Prerequisites" for the
full list. Minimum:

- `Contributor` on `sydney-pulse-rg-dev` + `DevPulseRG`
- `Role Based Access Control Administrator` on `sydney-pulse-rg-dev`
- `Key Vault Secrets Officer` on `sydney-pulse-kv-dev` (to seed secrets)

### Subscription context

```powershell
az account show --query name -o tsv   # expect: DevPulse subscription
az account set --subscription <id>    # if wrong
```

---

## Step 1 — Infrastructure deploy

Delegate to [`infra/DEPLOY.md`](../../infra/DEPLOY.md):

1. **Step 1 — What-if** (always run first)
2. **Step 2 — Deploy** (`az deployment group create`)
3. **Step 3 — Verify** (RG resources, Function App `Running`, Cosmos region, SB topic)
4. **Step 4 — Set App Insights daily cap to 1 GB**
5. **Step 5 — Seed Key Vault secrets** (`TfNswApiKey`, `AzureSignalRConnectionString`, `ServiceBusConnectionString`)
6. **Step 6 — Wire Event Grid webhooks** (state-writer + archiver subscriptions)

Return here when all 6 are done.

---

## Step 2 — Deploy Function App code

The Function App resource exists after Step 1 but contains no code.
This step publishes the .NET 8 isolated-worker build.

### Pre-flight

```powershell
cd C:\BUDDHIKA\SydPulse-P6\Sydney-Pulse
dotnet build functions/SydneyPulse.sln    # expect: 0 errors, 0 warnings
dotnet test  functions/SydneyPulse.sln    # expect: 55/55 pass on main
```

### Publish

```powershell
cd functions/SydneyPulse.Functions
dotnet clean
# Scorched-earth obj/bin wipe — the isolated-worker source generator
# drops Microsoft.Azure.Functions.Worker.Extensions.csproj into obj/.
# Without this wipe, `func` errors with "Expected 1 .csproj or .fsproj
# but found 2". Same gotcha as SP1-02's `func start`.
Remove-Item obj, bin -Recurse -Force -ErrorAction SilentlyContinue
func azure functionapp publish sydney-pulse-func-dev
```

The publish output prints a ".NET 10 EOL warning" for .NET 8 —
informational, ignore. Project is on .NET 8 LTS by design.

### Verify

- Azure portal → `sydney-pulse-func-dev` → Functions blade lists all 10:
  `Poller`, `StateWriter`, `Alerter`, `ArchiverIngest`, `ArchiverFlush`,
  `negotiate`, `Vehicles`, `Alerts`, `Routes`, `spike`
- App Insights → Live Metrics → telemetry flowing within ~60 s
- Functions stay Enabled (not Disabled by quota / config issue)

```powershell
az functionapp show -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --query state -o tsv
# expect: Running
```

---

## Step 3 — Tighten CORS for the spike client

Default CORS after Bicep deploy is `*`. Tighten to the local origin used
by `spike-deployed.html`:

```powershell
az functionapp cors remove -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --allowed-origins "*"
az functionapp cors add    -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --allowed-origins http://localhost:5500
```

Verify:

```powershell
az functionapp cors show -n sydney-pulse-func-dev -g sydney-pulse-rg-dev
# expect: allowedOrigins includes http://localhost:5500
```

Add other origins (e.g. the deployed Static Web App URL) once SP1-10 ships.

---

## Step 4 — Smoke verification

Quick checklist. For the full evidence pack (KQL queries, screenshots,
captured JSON fixtures) → [`dev-smoke-evidence.md`](dev-smoke-evidence.md).

| Layer | Check | Pass criterion |
|---|---|---|
| Poller | App Insights Live Metrics | request rate ~2/min for `Poller` operation |
| Event Grid | KQL `requests \| where operation_Name == "StateWriter"` | invocations climbing |
| Cosmos | Portal → `sydney-pulse-cosmos-dev` → Data Explorer → `vehicles` / `alerts` | docs landing; diverse `routeShortName` values |
| HTTP API | `curl https://sydney-pulse-func-dev.azurewebsites.net/api/routes?code=<FUNC_KEY>` | 200 + non-empty JSON |
| SignalR | Serve `spike-deployed.html` via `python -m http.server 5500`, browse to `http://localhost:5500/spike-deployed.html` | status "Connected to hub: vehicles"; `vehicleUpdated` payloads stream in within 30 s |

Function-level auth keys (for `?code=` on HTTP endpoints): portal →
`sydney-pulse-func-dev` → Functions → `<FunctionName>` → Function Keys.

If SignalR sees zero payloads despite Cosmos populating, check the
SignalR portal → Diagnostics → **Live Trace Tool**. See Debug Story #20
in `docs/sp1-16-debug-stories.md` for the group-vs-hub bug class to
rule out.

---

## Rollback

Three layers, smallest blast radius first.

### Code rollback (most common)

Re-publish a previous good build:

```powershell
git checkout <last-good-commit>
cd functions/SydneyPulse.Functions
dotnet clean
Remove-Item obj, bin -Recurse -Force -ErrorAction SilentlyContinue
func azure functionapp publish sydney-pulse-func-dev
git checkout main
```

If the previous build is healthy but the running instance is misbehaving
(stuck on a poisoned message, leaked connection, etc.), restart instead
of redeploying:

```powershell
az functionapp restart -n sydney-pulse-func-dev -g sydney-pulse-rg-dev
```

### Configuration rollback

App-settings change made via portal that broke something: revert via
portal Configuration blade, or:

```powershell
az functionapp config appsettings set -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --settings KEY=value
```

CORS reverts: re-add `*` temporarily to unblock testing, then re-tighten:

```powershell
az functionapp cors add -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --allowed-origins "*"
```

### Infrastructure rollback

Bicep change broke an Azure resource: re-run `az deployment group create`
with the prior `dev.bicepparam` (or revert the Bicep file via git first).
Bicep is idempotent.

Cosmos data + Storage blobs are **persistent** — they survive Bicep
redeploys. To wipe them, delete the container/account explicitly in the
portal (or via `az cosmosdb sql container delete` / `az storage container
delete`).

Full nuke: `az group delete -n sydney-pulse-rg-dev --yes` — then restart
this runbook from Step 1. Note: Key Vault soft-delete will hold the vault
name for 7 days after RG delete; either purge it (`az keyvault purge`) or
wait, or change the name.

---

## Cost & cleanup between sessions

The dev environment is cheap (~AUD $0.10–$0.50 per 30-min smoke) but the
Function App is the only piece with non-zero idle cost while Poller fires
every 30 s.

**Pause:**

```powershell
az functionapp stop -n sydney-pulse-func-dev -g sydney-pulse-rg-dev
```

Stops the Functions host — no Poller ticks, no Cosmos writes, no SignalR
broadcasts. Cosmos / SignalR / Storage / App Insights continue to exist
at their own (minimal) idle costs. Restart with `az functionapp start`.

**Tear down completely:**

```powershell
az group delete -n sydney-pulse-rg-dev --yes
# Optionally also purge the Key Vault (otherwise the name is held for 7 days)
az keyvault purge --name sydney-pulse-kv-dev --location australiaeast
```

See [`infra/DEPLOY.md`](../../infra/DEPLOY.md) "Cost notes" for a
per-resource cost breakdown.
