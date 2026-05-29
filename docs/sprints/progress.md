# Sprint 1 — Progress Tracker

Living document. Updated as items complete or status changes. Authoritative
for current sprint state; `sprint-1.md` remains the authoritative scope spec.

**Sprint window:** started 2026-05-29 (~10 days per `sprint-1.md`)
**Goal:** Public live URL with full event-driven pipeline and Angular live
dashboard, tagged `v0.1.0`.

## Legend

⬜ pending · 🔄 in progress · ✅ done · ⚠️ blocked

## Backlog status

| #      | Item                              | Status | Started     | Done        | Commits             |
|--------|-----------------------------------|--------|-------------|-------------|---------------------|
| SP1-01 | Repo + Azure bootstrap            | ✅     | 2026-05-29  | 2026-05-29  | `eded4fa`, `ac1cd0a` |
| SP1-02 | SignalR de-risking spike          | ✅     | 2026-05-29  | 2026-05-29  | `d4629c9`           |
| SP1-03 | Bicep skeleton                    | ✅     | 2026-05-29  | 2026-05-29  | see bundle commit   |
| SP1-04 | TfNswFeedClient                   | ✅     | 2026-05-29  | 2026-05-29  | see SP1-04 commit   |
| SP1-05 | Poller Function                   | ✅     | 2026-05-30  | 2026-05-30  | `aad975e` (PR #2)   |
| SP1-06 | State Writer Function             | ⬜     | —           | —           | —                   |
| SP1-07 | Alerter chain                     | ⬜     | —           | —           | —                   |
| SP1-08 | HTTP API                          | ⬜     | —           | —           | —                   |
| SP1-09 | Angular scaffolding (deeper)      | ⬜     | —           | —           | —                   |
| SP1-10 | Live dashboard                    | ⬜     | —           | —           | —                   |
| SP1-11 | Landing page                      | ⬜     | —           | —           | —                   |
| SP1-12 | GitHub Actions CI/CD              | ⬜     | —           | —           | —                   |
| SP1-13 | Sprint wrap → v0.1.0              | ⬜     | —           | —           | —                   |

## SP1-01 — Repo + Azure bootstrap ✅

Closed 2026-05-29. Commits `eded4fa` (scaffolding) and `ac1cd0a` (CLAUDE.md
workflow doc).

Landed:

- Monorepo folders: `/functions`, `/web`, `/infra/{modules,parameters}`,
  `/.github/workflows`.
- .NET 8 solution with `SydneyPulse.Functions` (isolated worker),
  `SydneyPulse.Core`, `SydneyPulse.Tests` (xUnit). `global.json` pins SDK
  to `8.0.127`. Build clean.
- Angular 18 standalone app with Tailwind v3, SCSS, routing, no SSR.
  `ng build` clean (~246 kB bundle).
- Root `package.json` with commitlint + husky; `commit-msg` hook verified
  end-to-end on both initial commits.
- `README.md` skeleton linking to existing docs.
- Azure: `Sydney-Pulse-Montly-Budget` ($40/month) created in portal —
  verification still pending propagation in Cost Management API.

Out of scope (deferred or substituted):

- **GitHub Projects board** → using Jira instead per user preference.
- **gh CLI auth** → CLI installed but PATH not refreshed in bash; needed
  for SP1-12 (workflow dispatch).

## SP1-02 — SignalR de-risking spike ✅

Closed 2026-05-29. Risk gate cleared — end-to-end SignalR confirmed locally.
SP1-03 (Bicep skeleton) is unblocked.

Done:

- RG `sydney-pulse-rg-dev` created in `australiaeast`.
- SignalR Service `sydney-pulse-signalr-dev` provisioned: `Free_F1`,
  Serverless mode. Verified via `az signalr show` — hostname
  `sydney-pulse-signalr-dev.service.signalr.net`.
- NuGet `Microsoft.Azure.Functions.Worker.Extensions.SignalRService` 2.0.1
  added to `SydneyPulse.Functions.csproj`.
- `local.settings.json` updated with `AzureSignalRConnectionString` (live
  key, user-managed) and `Host: { CORS: "*", CORSCredentials: false }`.
- Security hardening: `.claude/settings.json` deny rules block Claude tools
  from reading, editing, or writing any `**/local.settings.json`.
- SignalR primary key rotated via Azure portal 2026-05-29.
- `NegotiateFunction.cs` — POST `/api/negotiate`, returns `SignalRConnectionInfo`
  serialised as camelCase JSON (`url`, `accessToken`) so the SignalR JS
  client recognises the Azure redirect.
- `SpikeFunction.cs` — POST `/api/spike`, broadcasts `{"text":"hello"}` to
  hub `spike` via `[SignalROutput]`.
- `spike.html` — minimal browser client; connects via negotiate, prints
  incoming `newMessage` payloads to a log panel.
- Manual test procedure documented in this file under SP1-02.
- New CLAUDE.md "Code comments" convention — intent-communicating comments
  on all code Claude writes; rolled into this bundle commit.

Non-obvious decisions landed:

- `func start` requires `dotnet clean` first — `WorkerExtensions.csproj`
  in `obj/` causes a "found 2 .csproj" error on plain `func start`.
- `spike.html` must be served via HTTP (not `file://`) to avoid `null`
  origin CORS rejection.
- `NegotiateFunction` must explicitly serialise response as
  `HttpResponseData` with camelCase JSON — isolated worker does not
  auto-serialise a raw return type to the HTTP response body.

### Manual test procedure (SP1-02 spike)

**Prerequisites**

- `AzureSignalRConnectionString` pasted into
  `functions/SydneyPulse.Functions/local.settings.json` (get the Primary
  Connection String from Azure Portal → SignalR Service →
  `sydney-pulse-signalr-dev` → Keys).
- `local.settings.json` has a top-level `Host` object (not a flat key):
  ```json
  "Host": { "LocalHttpPort": 7071, "CORS": "*", "CORSCredentials": false }
  ```
- Node.js available (for the static file server).

**Steps**

1. Start the Functions host (from
   `functions/SydneyPulse.Functions/`):
   ```
   dotnet clean SydneyPulse.Functions.csproj && func start
   ```
   Wait until the console lists both `negotiate` and `spike` endpoints.

2. In a second terminal, serve the spike page from the project root:
   ```
   python -m http.server 5500
   ```

3. Open `http://localhost:5500/spike.html` in a browser.

4. In a third terminal, fire the broadcast:
   ```
   curl.exe -X POST http://localhost:7071/api/spike
   ```

**Pass criteria**

| Check | Expected |
|-------|----------|
| Browser status line | "Connected to hub: spike" |
| Log panel after curl | `[HH:MM:SS.mmm] {"text":"hello"}` appears within ~1 s |
| `func start` console | No errors on the negotiate or spike invocations |

**Known gotchas**

- `func start` without `dotnet clean` first fails with "found 2 .csproj
  files" — the `WorkerExtensions.csproj` in `obj/` is the culprit.
- Opening `spike.html` directly as a `file://` URL causes CORS failure
  (`null` origin); always serve via `python -m http.server`.
- `NegotiateFunction` must serialize the response with camelCase keys
  (`url`, `accessToken`) — the SignalR JS client does a case-sensitive
  lookup to detect the Azure redirect.

## SP1-03 — Bicep skeleton ✅

Closed 2026-05-29. All infra resources declared and `bicep build` compiles
with 0 errors. SP1-04 (TfNswFeedClient) is unblocked.

Done:

- `infra/main.bicep` — entry point; orchestrates all modules; role
  assignments inlined as a module (not in main scope) to satisfy Bicep's
  requirement that role assignment `name` values be pre-computable.
- `infra/modules/security.bicep` — Key Vault (Standard, RBAC-only, soft-delete).
- `infra/modules/observability.bicep` — Log Analytics (PerGB2018) +
  App Insights (workspace-based, 1 GB/day cap). Sampling rate moved to
  `host.json`; not a Bicep concern.
- `infra/modules/data.bicep` — Cosmos DB Serverless (`sydneyPulse` DB,
  `vehicles` container TTL 5 min, `alerts` container TTL 24 h, lat/lon
  excluded from index). Fresh account in `australiaeast` — not reusing
  `devpulse-events` (wrong region, operational coupling). Functions storage
  + Data Lake Gen2 with `archive` container.
- `infra/modules/messaging.bicep` — Event Grid custom topic
  (`CloudEventSchemaV1_0`) with state-writer, alerter, and archiver
  subscriptions. Alerter subscription destination references SB topic via
  computed `resourceId()` (avoids BCP165 cross-scope parent error).
- `infra/modules/servicebus-topic.bicep` — `sydney-pulse-alerts` topic +
  `alerter-sub` subscription on `devpulse-service-bus`. Deployed as a
  scoped module to `DevPulseRG`; namespace config untouched (ADR-0003).
- `infra/modules/compute.bicep` — Function App (Consumption Y1, .NET 8
  isolated, Windows). System-assigned MI. `AzureWebJobsStorage` uses
  identity-based access (no connection string). Secrets via
  `@Microsoft.KeyVault(VaultName=...;SecretName=...)` references.
- `infra/modules/frontend.bicep` — SignalR Free_F1 (Serverless mode,
  brings SP1-02 manual provision under Bicep). Static Web App (Free tier,
  Custom build provider for GitHub Actions in SP1-12).
- `infra/modules/role-assignments.bicep` — All RBAC: KV Secrets User,
  Cosmos Built-in Data Contributor, EventGrid Data Sender, Storage Blob
  Data Contributor (Data Lake), Storage Blob Owner + Queue + Table
  Contributor (func storage for identity-based AzureWebJobsStorage).
- `infra/parameters/dev.bicepparam` — real Service Bus names
  (`devpulse-service-bus` / `DevPulseRG`) per ADR-0003.
- `infra/parameters/prod.bicepparam` — same shape, prod values.

Non-obvious decisions:

- Storage account names are alphanumeric-only (`sydpulsestor{env}`,
  `sydpulsedlsa{env}`) — Azure rejects hyphens in storage account names.
  The `sydney-pulse-storage-{env}` convention in `infra/CLAUDE.md` was
  corrected here.
- Cosmos DB not reused from `devpulse-events`: wrong region
  (`australiasoutheast` vs `australiaeast`) and operational coupling risk.
  New account costs ~$0.50–$2 AUD/month at portfolio scale (no fixed fee
  for Serverless).
- Event Grid webhook endpoint URLs are placeholder strings for the
  state-writer and archiver subscriptions — they will be updated once the
  Function App URL is known after first deployment.
- `bicep build` emits two BCP081 warnings on old preview API versions for
  App Insights billing resources — known Bicep type-library gap, safe to
  ignore; resources deploy correctly at runtime.

## SP1-04 — TfNswFeedClient ✅

Closed 2026-05-29. Build clean (0 errors, 0 warnings). All 3 unit tests pass.

Landed:

- `SydneyPulse.Core/TfNsw/GtfsRealtime.proto` — slim proto3 definition for
  `FeedMessage`, `VehiclePosition`, `Alert`. Field numbers match the official
  GTFS-RT spec so TfNSW's proto2-encoded binary parses correctly.
- `TfNswOptions.cs` — strongly-typed config (`ApiKey`, `BaseUrl`,
  `VehicleModes[]`); bound via `IOptions<TfNswOptions>`.
- `RouteInfo.cs` — immutable record for static route metadata.
- `Events/VehicleUpdate.cs` + `Events/ServiceAlert.cs` — event records
  matching the `VehicleUpdate.v1` / `ServiceAlert.v1` CloudEvent shapes
  from `docs/api.md`.
- `ITfNswFeedClient.cs` — interface with 3 methods: `GetVehiclePositionsAsync`,
  `GetServiceAlertsAsync`, `GetRoutesAsync`.
- `TfNswFeedClient.cs` — implementation: HTTP fetch → protobuf decode →
  route enrichment → 1-hour in-memory cache per mode (ADR-0009).
- `Tests/Unit/TfNswFeedClientTests.cs` — 3 tests: vehicle position mapping,
  cache hit (second call makes no HTTP request), CSV colour normalisation.

Non-obvious decisions:

- Proto3 syntax used instead of the official proto2 — wire format is
  compatible when field numbers match, avoids `required` field complexity
  with `Google.Protobuf`.
- Auth header (`apikey`) set per-request via `HttpRequestMessage` in
  `FetchBytesAsync`. Polly resilience handler (retry on 429/503, circuit
  breaker) is NOT in the client itself — it goes on the named `HttpClient`
  in `Program.cs` DI registration (SP1-05).
- Double-checked locking in `GetRoutesAsync`: fast path skips the
  `SemaphoreSlim` for warm cache; lock only taken on miss to prevent
  duplicate concurrent downloads.
- CSV parser handles double-quoted fields — `route_long_name` can contain
  commas in some GTFS feeds.

## SP1-05 — Poller Function ✅

Closed 2026-05-30. PR #2 squash-merged. All 6 unit tests pass (3 new + 3
existing TfNswFeedClient tests).

Landed:

- `SydneyPulse.Functions/EventGridOptions.cs` — strongly-typed config for
  `EventGrid__TopicEndpoint` app setting; bound via `IOptions<EventGridOptions>`.
- `Program.cs` updated — registers `EventGridOptions` and a singleton
  `EventGridPublisherClient` using `DefaultAzureCredential` (Managed Identity
  in Azure, `az login` locally).
- `Functions/PollerFunction.cs` — `TimerTrigger("*/30 * * * * *")`, iterates
  `TfNswOptions.VehicleModes`, fetches GTFS-RT feeds via `ITfNswFeedClient`,
  publishes `VehicleUpdate.v1` and `ServiceAlert.v1` CloudEvents in batches.
  Empty feeds are skipped (no empty batch sent to Event Grid).
- `Tests/Unit/PollerFunctionTests.cs` — 3 unit tests: vehicles published as
  correct type, empty feed guard, alert type + source verified.
- NuGet packages added: `Azure.Messaging.EventGrid` 4.25.0,
  `Azure.Identity` 1.13.1, `Microsoft.Azure.Functions.Worker.Extensions.Timer`
  4.3.0.

Non-obvious decisions:

- CloudEvent type strings (`com.sydneypulse.VehicleUpdate.v1`,
  `com.sydneypulse.ServiceAlert.v1`) must match the `includedEventTypes` filter
  values in `messaging.bicep` exactly — any drift silently drops events.
- Event routing: `VehicleUpdate.v1` → state-writer (Cosmos + SignalR vehicles
  group) + archiver. `ServiceAlert.v1` → alerter (SB topic → Alerter Fn →
  SignalR alerts group) + archiver. SignalR for vehicles is an output of the
  State Writer Function, not a separate Event Grid subscriber.
- First sprint item to use the feature branch + PR cycle (documented in
  `CLAUDE.md`).

## SP1-06 through SP1-13

Not started. Refer to `sprint-1.md` for scope and per-item description.

## Decisions logged

- **2026-05-29 — Tracking tool.** Jira boards, not GitHub Projects.
  `docs/sprints/*` remain authoritative for sprint backlog and progress.
- **2026-05-29 — Collaboration pattern.** Strict step-by-step. User runs
  every Azure mutation (portal or CLI); Claude provides instructions and a
  read-only verification command. No parallel code work; one file change
  per turn, announced before edit. Documented in `CLAUDE.md` under "Working
  with Claude Code".
- **2026-05-29 — Service Bus reuse confirmed.** Will reuse
  `devpulse-service-bus` in `DevPulseRG` (Standard tier) per ADR-0003. Real
  names go only in `infra/parameters/dev.bicepparam` (SP1-03); docs stay
  generic.
- **2026-05-29 — Secrets policy.** `local.settings.json` is gitignored
  AND permission-denied from Claude tools (`.claude/settings.json`) to
  prevent leakage via the harness's file-modification reminder mechanism.
  SP1-03 will layer in Key Vault references for both cloud and local.
- **2026-05-30 — Bicep deploy completed.** `az deployment group create`
  succeeded (`provisioningState: Succeeded`). All 24 resources live in
  `sydney-pulse-rg-dev` and `DevPulseRG`. Post-deploy steps also done:
  App Insights 1 GB/day cap set; all three Key Vault secrets seeded.
- **2026-05-30 — Microsoft.AlertsManagement provider registered.**
  Azure auto-creates a Failure Anomalies alert rule when App Insights is
  provisioned. It failed with `MissingSubscriptionRegistration` because
  `Microsoft.AlertsManagement` was not registered on this subscription.
  Fixed via `az provider register --namespace Microsoft.AlertsManagement`.
  Not a Bicep concern — platform-side behaviour.
- **2026-05-30 — KV Secrets Officer role required for developer account.**
  Key Vault is RBAC-only; Bicep only grants the Function App MI access.
  Developer account needs `Key Vault Secrets Officer` on the vault to seed
  secrets. Documented in `infra/DEPLOY.md` prerequisites and Step 5.
- **2026-05-30 — host.json sampling fixed at 5%.** Adaptive sampling was
  enabled but unbounded. Set `minSamplingPercentage` and
  `maxSamplingPercentage` to 5.0 to pin the rate per CLAUDE.md constraint.
  Takes effect when Function App code is deployed in SP1-12.

## Risks / open items

| Risk                                                   | Mitigation                                                                | Owner   |
|--------------------------------------------------------|---------------------------------------------------------------------------|---------|
| `gh` CLI not on bash PATH                              | Reopen terminal or fix PATH; needed for SP1-12 (workflow dispatch)        | User    |
| Event Grid webhook URLs are placeholders               | Update state-writer + archiver subscriptions after Function App deployed   | Claude  |
| SignalR Free SKU caps (20 conns, 20k msgs/day)         | Acceptable per ADR-0008; load-test forbidden                              | —       |
| TfNSW API quota (5 rps, 60k/day)                       | Polly resilience handler on named HttpClient wired — mitigated (SP1-05)   | —       |

## Update protocol

When an item closes:

1. Flip the row's status to ✅, fill in `Done` date and commit hashes.
2. Add a short prose section above with what landed and any deferrals.
3. Move follow-ups to "Risks / open items" if non-blocking.

When an item is blocked:

1. Flip to ⚠️ with a note in "Risks / open items" describing the blocker
   and what unblocks it.

## Next session handoff (2026-05-30)

SP1-05 complete and merged. SP1-06 (State Writer Function) is next.

### Where we are

- Bicep deployed to `sydney-pulse-rg-dev` — all resources healthy.
- App Insights daily cap: 1 GB/day. Sampling: fixed 5% in `host.json`.
- Key Vault secrets seeded: `TfNswApiKey`, `AzureSignalRConnectionString`,
  `ServiceBusConnectionString`.
- Poller Function live on `main` — publishes `VehicleUpdate.v1` and
  `ServiceAlert.v1` CloudEvents to Event Grid every 30 seconds.
- Event Grid `state-writer` and `archiver` webhook subscriptions still
  placeholder — update after Function App URL is known (Step 6 of
  `infra/DEPLOY.md`, deferred to SP1-12).
- Feature branch + PR workflow now active for all sprint items (SP1-06
  onwards). See `CLAUDE.md` for the branch naming convention and merge steps.

### Resume sequence

1. Follow session start protocol per `CLAUDE.md`.
2. **Start SP1-06 — State Writer Function** per `sprint-1.md`.

### Standing operating rules

- User runs all Azure mutations. Claude provides instructions and
  read-only verification only.
- One file change per turn. Announce file and reason before editing.
- Claude cannot read/write `**/local.settings.json` (deny rule).
- Windows / PowerShell — single-line commands only.
- Code comments convention active on all source files Claude writes.
- Feature branches + PR for all sprint items from SP1-05 onwards.
