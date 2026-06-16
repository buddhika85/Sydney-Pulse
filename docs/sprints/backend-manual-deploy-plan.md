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

## Strategy — why real Azure dev (not local Docker)

SP1-16 deploys to and verifies against the **real `sydney-pulse-rg-dev`
resource group**, not local containers. Two reasons make this not a
choice but the only viable path:

- **Event Grid has no official local emulator.** You cannot run the
  fans-out-to-three-subscribers pipeline locally end-to-end no matter
  how many containers you spin up.
- **Azure SignalR Serverless mode has no emulator either.** The whole
  Negotiate → access token → cloud WebSocket flow requires the cloud
  service. SP1-02 used real cloud SignalR for exactly this reason.

The other reason: the *artefact* SP1-16 produces — App Insights traces,
Cosmos Data Explorer screenshots, Live Metrics — only exists in real
Azure. That's the interview material.

Cost is not a blocker. The dev environment uses Consumption + Serverless
+ Free SKUs only; a 30-minute smoke window costs roughly **AUD $0.10 –
$0.50**.

### Emulator availability across our stack

| Service | Local emulator? | SP1-16 (cloud dev) | Later (SP2-01 / CI / pure-local) |
|---|---|---|---|
| Azure Functions runtime | Yes — `func start` (Core Tools) | Real `sydney-pulse-func-dev` Function App | `func start` for local debug |
| Cosmos DB | Yes — Linux preview Docker container | Real `sydney-pulse-cosmos-dev` (Serverless) | Cosmos emulator container |
| Storage / Data Lake Gen 2 | Yes — **Azurite** (HNS supported) | Real `sydpulsedlsadev` + `sydpulsestordev` | Azurite |
| Service Bus | Yes — official emulator container (since 2024) | Real shared `devpulse-service-bus` topic | SB emulator container |
| **Event Grid** | **No official emulator** | Real custom topic `transit-events` in `sydney-pulse-rg-dev` | Mocks at unit-test level only |
| **Azure SignalR (Serverless)** | **No emulator** | Real `sydney-pulse-signalr-dev` (Free SKU) | Real cloud SignalR even in local dev |
| Key Vault | No emulator | Real KV + Managed Identity | env vars in `local.settings.json` |
| Application Insights | No (cloud-only telemetry) | Real workspace + dev App Insights | console logs |

### Where Docker emulators DO come in (not SP1-16)

- **SP2-01** (deferred from SP1-15): Azurite + Cosmos emulator + Service
  Bus emulator integration tests in CI. Catches drift between Moq mocks
  and real wire behaviour. The `ArchiveManifestBlobName` mis-reference
  we caught manually during SP1-15 review is exactly the kind of bug
  SP2-01 will catch automatically.
- Local debug during SP1-09 / SP1-10 frontend dev: point the Angular dev
  server at the deployed dev Function App (`https://sydney-pulse-func-dev.azurewebsites.net`),
  not local. Simpler than spinning up the whole container fleet just to
  see one HTTP response.

### The hybrid pattern already in use

Unit tests use Moq mocks for Cosmos / Event Grid / Service Bus clients
(`SydneyPulse.Tests` — 55 tests). The SP1-02 SignalR spike used real
cloud SignalR end-to-end. SP1-16 deploys the rest of the pipeline to
the same real cloud dev. Local-only "everything in Docker" isn't a
model this project ever adopts — the cloud-native services have no
faithful local equivalents and the cost of running everything in real
dev is negligible.

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

### What's about to be deployed (reference)

Ten Functions in workflow order — event pipeline first, then HTTP API,
then the SP1-02 spike (kept for SignalR connectivity smoke; not in the
production flow).

| # | Function | Trigger | Input bindings | Output bindings | Input parameters | What it does |
|---|---|---|---|---|---|---|
| 1 | `Poller` | `TimerTrigger("*/30 * * * * *")` | — | — *(injected `EventGridPublisherClient`)* | `TimerInfo timer` | • Iterates configured TfNSW modes (trains, buses, ferries, metro, light rail)<br>• Fetches GTFS-RT vehicle positions + service alerts via `TfNswFeedClient`<br>• Publishes `VehicleUpdate.v1` + `ServiceAlert.v1` CloudEvents to Event Grid<br>• Skips empty feeds (no empty batch sent) |
| 2 | `StateWriter` | `EventGridTrigger` | — | `[SignalROutput(HubName = VehiclesSignalRHub)]` → `SignalRMessageAction?` | `VehicleUpdate update` *(deserialized from CloudEvent)* | • Receives `VehicleUpdate.v1` from Event Grid `state-writer` subscription<br>• Stale-write guard: reads existing doc, drops if stored timestamp ≥ incoming<br>• Upserts `VehicleDocument` to Cosmos `vehicles` (partition key `routeShortName`)<br>• Broadcasts `vehicleUpdated` to SignalR `vehicles` group |
| 3 | `Alerter` | `ServiceBusTrigger("sydney-pulse-alerts", "alerter-sub", Connection = ServiceBus__fullyQualifiedNamespace)` | — | `[SignalROutput(HubName = AlertsSignalRHub)]` → `SignalRMessageAction?` | `string messageBody` *(raw SB message — full CloudEvent envelope)* | • EG fans `ServiceAlert.v1` events into the Service Bus topic<br>• Unwraps the CloudEvent JSON envelope (`CloudEvent.ParseMany`)<br>• Deserialises `ServiceAlert` (case-insensitive — Azure SDK camelCase)<br>• Upserts `AlertDocument` to Cosmos `alerts` (TTL 24 h)<br>• Broadcasts `alertReceived` to SignalR `alerts` group |
| 4 | `ArchiverIngest` | `EventGridTrigger` | — | — *(writes via injected `IPendingBlobStore`)* | `CloudEvent cloudEvent` | • Receives every `VehicleUpdate.v1` + `ServiceAlert.v1` event<br>• Maps to unified flat `ArchiveEvent` (24 columns, discriminated by `eventType`)<br>• Appends one JSONL line to `pending/{HivePartition.ForHour(SourceTimestamp)}/events.jsonl`<br>• AppendBlock is atomic; duplicates tolerated and deduped at flush by `EventId` (ADR-0012) |
| 5 | `ArchiverFlush` | `TimerTrigger("0 */5 * * * *")` | — | — *(writes via `BlobServiceClient` + `IPendingBlobStore`)* | `TimerInfo timer` | • Lists every partition path in `pending/` (streamed `IAsyncEnumerable`)<br>• Filters to those past `PartitionGraceMinutes` (closeable check)<br>• For each: read JSONL → dedupe by `EventId` → write Parquet → write `_manifest.json` (the commit point) → delete pending blob<br>• `overwrite: true` everywhere → re-flush after a crash is idempotent |
| 6 | `negotiate` | `HttpTrigger(Anonymous, "post")` | `[SignalRConnectionInfoInput(HubName = VehiclesSignalRHub)] vehiclesInfo` + `[SignalRConnectionInfoInput(HubName = AlertsSignalRHub)] alertsInfo` | — *(returns `HttpResponseData`)* | Query: `?hub=vehicles\|alerts` *(default `vehicles`)* | • `POST /api/negotiate` — returns short-lived SignalR access token + URL<br>• Both hubs bound as inputs (HubName must be compile-time const); runtime picks one via `?hub=`<br>• Angular calls twice on startup, once per hub<br>• Response uses lowercase `url` / `accessToken` so the SignalR JS client detects the Azure redirect |
| 7 | `Vehicles` | `HttpTrigger(Anonymous, "get", Route = "vehicles")` | — | — *(returns `HttpResponseData`)* | Query: `?mode=` + `?routeShortName=` *(both optional)* | • `GET /api/vehicles` — current vehicle state from Cosmos<br>• `routeShortName` set → single-partition read (most RU-efficient)<br>• `mode` set → cross-partition WHERE filter<br>• Neither set → full container scan<br>• 5 s in-process `IMemoryCache` keyed by full query string + `Cache-Control: public, max-age=5` for CDN / browser |
| 8 | `Alerts` | `HttpTrigger(Anonymous, "get", Route = "alerts")` | — | — *(returns `HttpResponseData`)* | — | • `GET /api/alerts` — current service alerts from Cosmos<br>• Cross-partition scan `ORDER BY c.receivedAt DESC`<br>• Container TTL 24 h auto-purges expired alerts — no extra filter needed |
| 9 | `Routes` | `HttpTrigger(Anonymous, "get", Route = "routes")` | — | — *(returns `HttpResponseData`)* | — | • `GET /api/routes` — GTFS static route metadata for every configured mode<br>• Served from `TfNswFeedClient`'s in-process 1 h cache (ADR-0009) — zero Cosmos RUs<br>• Projected to API contract fields (`routeShortName`, `routeLongName`, `routeColor`, `mode`) |
| 10 | `spike` *(SP1-02, not production)* | `HttpTrigger(Anonymous, "post")` | — | `[SignalROutput(HubName = "spike")]` → `SignalRMessageAction` | — | • De-risk endpoint kept from SP1-02 SignalR spike<br>• `POST /api/spike` → broadcasts `{ "text": "hello" }` to the `spike` hub<br>• Useful as the absolute-minimum SignalR connectivity smoke before wiring real hubs |

### Deploy

`dotnet clean` first — the isolated-worker source generator drops
`Microsoft.Azure.Functions.Worker.Extensions.csproj` into `obj/` during
build, and `func` counts it as a second project ("Expected 1 .csproj
or .fsproj but found 2"). Same gotcha as SP1-02's `func start`.

`dotnet clean` alone sometimes leaves the generated csproj behind, so
follow it with a scorched-earth `obj/` + `bin/` wipe to be safe.

```powershell
cd functions/SydneyPulse.Functions
dotnet clean
# scorched-earth — guarantees the generator's stray csproj is gone
Remove-Item obj, bin -Recurse -Force -ErrorAction SilentlyContinue
func azure functionapp publish sydney-pulse-func-dev
```

The publish output will print a ".NET 10 EOL warning" for .NET 8 —
informational, ignore. Project is on .NET 8 LTS by design.

**Verify after deploy:**

- Azure portal → `sydney-pulse-func-dev` → Functions blade lists all 10:
  `Poller`, `StateWriter`, `Alerter`, `ArchiverIngest`, `ArchiverFlush`,
  `negotiate`, `Vehicles`, `Alerts`, `Routes`, `spike`.
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

> **Descope (2026-06-16):** D.5 (ArchiverIngest) and D.6 (ArchiverFlush)
> moved out of SP1-16 to Sprint 2 [SP-19](https://gsoft85512.atlassian.net/browse/SP-19)
> as a pre-Analytics-view de-risk. Sprint 1's frontend deliverable is the
> Commuter Dashboard (SP1-10), which reads via HTTP API + SignalR — not from
> the Parquet archive. The Archiver chain feeds the Analytics view (Sprint 3),
> so its smoke is correctly paired with that sprint's pre-flight. SP1-16
> verifies D.1–D.4, D.7, D.8 only.

| # | Component | How to verify | Pass criterion |
|---|---|---|---|
| D.1 | Poller | App Insights → traces → filter `cloud_RoleName == "sydney-pulse-func-dev" and operation_Name == "Poller"` | Invocation every 30s, no failures |
| D.2 | Event Grid | KQL: `traces \| where message contains "Published" \| summarize count() by bin(timestamp, 1m)` | Counts > 0 for last 5 min |
| D.3 | State Writer | Cosmos Data Explorer → `sydneyPulse > vehicles` | Docs landing; `sourceTimestamp` increasing on re-read |
| D.4 | Alerter chain | Service Bus → `sydney-pulse-alerts > alerter-sub` → peek messages; Cosmos `alerts` container | Messages flowing through SB; alert docs in Cosmos |
| ~~D.5~~ | ~~Archiver Ingest~~ | **DESCOPED → [SP-19](https://gsoft85512.atlassian.net/browse/SP-19)** (Sprint 2 row 12) | — |
| ~~D.6~~ | ~~Archiver Flush~~ | **DESCOPED → [SP-19](https://gsoft85512.atlassian.net/browse/SP-19)** (Sprint 2 row 12) | — |
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
