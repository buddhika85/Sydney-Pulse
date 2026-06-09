# SP1-16 — Backend manual deploy plan

Detailed execution plan for [SP1-16](sprint-1.md) — backend visibility
via manual deploy + end-to-end smoke verification. Linked from
[sprint-1.md](sprint-1.md) and [progress.md](progress.md). Jira ticket:
[SP-17](https://gsoft85512.atlassian.net/browse/SP-17).

**Why this exists:** the CLAUDE.md TDD method-loop workflow doesn't fit
this item — it's deploy + verify + document, not code-with-tests. This
file replaces that workflow with 8 ordered phases. Each phase ends at a
review stop-point.

## Goal recap

Prove every backend component works in real Azure (dev) before touching
the Angular UI. Produce a reproducible deploy recipe + evidence pack that
SP1-12 (CI/CD) will automate.

## Ordering

Revised order in Sprint 1:
**SP1-16** → SP1-09 → SP1-12 → SP1-10 → SP1-11 → SP1-13.

No frontend dependency — SignalR smoke uses `spike.html` from SP1-02,
not the Angular app.

## Pre-decision (before Phase A)

- **Phase C webhook wiring approach:** Bicep re-deploy (preferred — keeps
  IaC as source of truth) vs `az eventgrid event-subscription update`
  (faster but drifts from IaC). Default: Bicep re-deploy.

---

## Phase A — Pre-flight checks (~15 min)

Clear-the-decks before deploy. All commands are read-only.

| Step | Command | Pass criterion |
|---|---|---|
| A.1 | `gh auth status` | Logged in as `buddhika85`, scopes include `repo` |
| A.2 | `az account show` | `name` is the DevPulse subscription |
| A.3 | `az group show -n sydney-pulse-rg-dev` | RG exists in `australiaeast` |
| A.4 | `az functionapp show -n sydney-pulse-func-dev -g sydney-pulse-rg-dev --query state` | Returns `"Running"` |
| A.5 | `dotnet build functions/SydneyPulse.sln` | 0 errors, 0 warnings |
| A.6 | `dotnet test functions/SydneyPulse.sln` | 55/55 pass on `main` |

**Stop point.** If any step fails, fix before Phase B.

---

## Phase B — Deploy Function App code (~45 min)

The Function App resource exists (SP1-03) but no code has ever been
deployed.

```powershell
cd functions/SydneyPulse.Functions
func azure functionapp publish sydney-pulse-func-dev --csharp
```

**Verify after deploy:**

- Azure portal → `sydney-pulse-func-dev` → Functions blade lists all 9:
  `negotiate`, `spike`, `Poller`, `StateWriter`, `Alerter`,
  `ArchiverIngest`, `ArchiverFlush`, `Vehicles`, `Alerts`, `Routes`.
- Note the Function App **default hostname** (e.g.
  `sydney-pulse-func-dev.azurewebsites.net`) — needed in Phase C.
- App Insights → Live Metrics → confirm telemetry is flowing.

**Stop point.** Confirm function list matches expectations before Phase C.

---

## Phase C — Wire deferred Event Grid webhook URLs (~30 min)

Two subscriptions still hold placeholder URLs from SP1-03:
`state-writer` and `archiver`. Fix now that the Function App hostname
exists.

**Fetch the EG system key** (required for webhook auth):

```powershell
az functionapp keys list `
  -n sydney-pulse-func-dev `
  -g sydney-pulse-rg-dev `
  --query "systemKeys.eventgrid_extension_System_Key" -o tsv
```

**Approach 1 (preferred): Bicep re-deploy with parameters populated**

- Update deploy command (or wrapper script) so `funcAppDefaultHostname`
  and `funcAppEventGridSystemKey` parameters are passed.
- Re-run `az deployment group create` against `sydney-pulse-rg-dev`.
- Verify:

  ```powershell
  az eventgrid event-subscription show `
    --name state-writer `
    --source-resource-id <eg-topic-resource-id>
  ```

  `destination.endpointUrl` should be real, not placeholder.

**Approach 2 (faster, drifts from IaC):**

```powershell
az eventgrid event-subscription update --name state-writer --endpoint <real-url>
az eventgrid event-subscription update --name archiver --endpoint <real-url>
```

**Stop point.** Both subscriptions show real endpoint URLs before Phase D.

---

## Phase D — End-to-end smoke verification (~2-3 hrs)

The biggest phase. Verify each backend component end-to-end in Azure.

| # | Component | How to verify | Pass criterion |
|---|---|---|---|
| D.1 | Poller | App Insights → traces → filter `cloud_RoleName == "sydney-pulse-func-dev" and operation_Name == "Poller"` | Invocation every 30s, no failures |
| D.2 | Event Grid | KQL: `traces \| where message contains "Published" \| summarize count() by bin(timestamp, 1m)` | Counts > 0 for last 5 min |
| D.3 | State Writer | Cosmos Data Explorer → `sydneyPulse > vehicles` | Docs landing; `sourceTimestamp` increasing on re-read |
| D.4 | Alerter chain | Service Bus → `sydney-pulse-alerts > alerter-sub` → peek messages; Cosmos `alerts` container | Messages flowing through SB; alert docs in Cosmos |
| D.5 | Archiver Ingest | Storage Explorer → `sydpulsedlsadev > pending/yyyy=.../MM=.../dd=.../HH=.../events.jsonl` | Append blob present, size growing |
| D.6 | Archiver Flush | After 5+ min: `sydpulsedlsadev > archive/yyyy=.../MM=.../dd=.../HH=.../` | `.parquet` + `_manifest.json` present; manifest valid |
| D.7 | HTTP API | `curl https://sydney-pulse-func-dev.azurewebsites.net/api/vehicles?routeShortName=T1` `curl .../api/alerts` `curl .../api/routes` | 200; non-empty JSON; `Cache-Control` header present on `/api/vehicles` |
| D.8 | SignalR | Edit `spike.html` — point negotiate URL at deployed `/api/negotiate?hub=vehicles`; open in browser via `python -m http.server` | Browser status: "Connected"; log panel receives `vehicleUpdated` events |

**Note on D.7:** Function-level keys may be required for HTTP endpoints
depending on `AuthorizationLevel`. Use `?code=<func-key>` if 401.

**Stop point.** All 8 sub-items pass before Phase E.

---

## Phase E — Evidence pack (~1 hr)

New file: `docs/runbooks/dev-smoke-evidence.md`.

Sections:

- App Insights Live Metrics screenshot (shows live request rate)
- App Insights traces — sample KQL queries + result screenshots
- Cosmos Data Explorer — `vehicles` + `alerts` container screenshots
- Storage Explorer — `pending/` (mid-window) and `archive/` (post-flush) screenshots
- Function App → Functions blade screenshot (invocation counts)
- curl output samples for `/api/vehicles`, `/api/alerts`, `/api/routes`
- `spike.html` screenshot mid-broadcast

This is the artefact pack you show in interviews ("here's it running
in Azure").

**Stop point.** Evidence file complete before Phase F.

---

## Phase F — Reproducible deploy runbook (~30 min)

New file: `docs/runbooks/manual-deploy-dev.md`.

Structure:

1. Prerequisites (gh CLI, az CLI, dotnet SDK, Azure Functions Core Tools)
2. Required Azure RBAC (KV Secrets Officer, Cosmos Data Contributor)
3. Step-by-step commands — copy from Phases A–C with expected output
4. Verification checklist — copy from Phase D
5. Rollback notes (re-publish previous build, or `az functionapp restart`)

**Acceptance:** a second deploy run purely from the runbook succeeds
without referring back to this plan.

**Stop point.** Runbook complete before Phase G.

---

## Phase G — Quiz capture (~30 min)

Three Q&As, both formats per `docs/modes.md` two-doc system:

1. *"How does Managed Identity replace connection strings here, and what
   RBAC roles does the Function App need?"*
2. *"Walk me through a KQL query that finds Poller failures in the last
   hour."*
3. *"What gotcha hit you on first `func azure functionapp publish`?"*
   (write up whatever surprised you — that's the interview gold)

- Mechanical bullets → `C:\BUDDHIKA\SydPulse-P6\SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx`
- Story version → `docs/interview-prep.md` (gitignored)

**Stop point.** Both docs updated.

---

## Phase H — Wrap

1. Feature branch: `feat/sp1-16-backend-visibility`
2. Commit per phase (or per significant deliverable) — Conventional Commits
3. Push, open PR via GitHub MCP `create_pull_request`
4. PR contents:
   - `docs/runbooks/dev-smoke-evidence.md` (new)
   - `docs/runbooks/manual-deploy-dev.md` (new)
   - Any Bicep param tweaks from Phase C
   - `docs/sprints/sp1-16-plan.md` (this file) — moves with the PR if not
     already on `main`
5. After squash-merge:
   - Local `main` pull confirmed
   - Housekeeping commit to `main`: flip SP1-16 row to ✅ in
     `progress.md`, add prose section, update handoff
   - Propose Jira SP-17 → Done transition, wait for approval
   - On approval: transition + completion comment via MCP

## Estimate

~1.5 days. Realistic break:

- Half-day: Phases A → C (deploy + webhook wiring)
- Full day: Phases D → F (smoke + evidence + runbook)
- One hour: Phases G + H (quiz + wrap)

## Out of scope (deferred)

- Prod RG / deployment slot / blue/green swap → Sprint 2 (SP2-03 / SP2-04)
- CI/CD automation → SP1-12
- Production load testing → never (SignalR Free SKU 20-conn cap)
- Frontend smoke → SP1-09 / SP1-10

## Risk callouts

- `gh` CLI PATH was flagged as a risk in `progress.md` — verify in Phase A.1
- Function-level auth keys may be required for HTTP endpoints in Phase D.7
- `func azure functionapp publish` from PowerShell can hang on the upload
  step — if so, retry from a fresh shell or use `--build remote`
- Cosmos RU budget: smoke test should run for ≤30 min total to stay
  inside Serverless free-ish range. Stop background traffic after Phase D.
