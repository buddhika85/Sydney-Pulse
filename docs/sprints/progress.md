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
| SP1-06 | State Writer Function             | ✅     | 2026-05-30  | 2026-05-30  | PR #3 squash-merged |
| SP1-07 | Alerter chain                     | ✅     | 2026-05-30  | 2026-05-30  | PR #4 squash-merged |
| SP1-08 | HTTP API                          | ✅     | 2026-05-30  | 2026-05-31  | PR #5 squash-merged |
| SP1-15 | Archiver Function                 | ✅     | 2026-06-04  | 2026-06-05  | `b04b766` (PR #7 squash-merged) |
| SP1-16 | Backend visibility (manual deploy + smoke) — [plan](backend-manual-deploy-plan.md) | ✅     | 2026-06-09  | 2026-06-20  | many on main + PR #8 (`cbe32eb`) |
| SP1-09 | Angular scaffolding (deeper)      | ✅     | 2026-06-20  | 2026-06-24  | `fcb4466` (PR #9 squash-merged) |
| SP1-14 | Code review + interview-prep quiz (always-on) | 🔄     | 2026-06-01  | —           | —                   |
| SP1-12 | GitHub Actions CI/CD              | ✅     | 2026-06-25  | 2026-06-29  | `6256c39` (PR #10 squash-merged) |
| SP1-10 | Live dashboard                    | ✅     | 2026-06-30  | 2026-07-10  | `f10a434` (PR #11 squash-merged) |
| SP1-11 | Landing page                      | ✅     | 2026-07-10  | 2026-07-11  | `29dfccc` (PR #12 squash-merged) |
| SP1-13 | Sprint wrap → v0.1.0              | ✅     | 2026-07-11  | 2026-07-16  | `a9634ef` (PR #13 squash-merged) + wrap commits on `main` (`20518d4` DomSanitizer/evidence config fix) |

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

## SP1-06 — State Writer Function ✅

Closed 2026-05-30. PR #3 squash-merged. All 9 unit tests pass (6 existing + 3 new).

Landed:

- `SydneyPulse.Core/Cosmos/VehicleDocument.cs` — Cosmos document model for latest
  vehicle position. Partition key `routeShortName`, document id `vehicleId` (one
  doc per vehicle, upsert overwrites). CamelCase serialization via `CosmosClientOptions`
  — no Newtonsoft.Json dependency in Core.
- `SydneyPulse.Functions/CosmosOptions.cs` — strongly-typed config for Cosmos endpoint,
  following the same Options pattern as `EventGridOptions`.
- `SydneyPulse.Functions/Program.cs` — `CosmosClient` singleton registered with
  `DefaultAzureCredential` and CamelCase serializer. Endpoint sourced from
  `Cosmos__AccountEndpoint` app setting via `IOptions<CosmosOptions>`.
- `SydneyPulse.Functions/Functions/StateWriterFunction.cs` — EventGrid trigger on
  `VehicleUpdate.v1`. Stale-write guard reads existing document first; drops event if
  stored timestamp ≥ incoming (handles Event Grid at-least-once delivery). Upserts to
  Cosmos `vehicles` container, then broadcasts `vehicleUpdated` to SignalR `vehicles` group.
- `SydneyPulse.Tests/Unit/StateWriterFunctionTests.cs` — 3 unit tests: new vehicle
  (NotFound path), stale event dropped, newer event upserts and broadcasts.
- NuGet: `Microsoft.Azure.Cosmos 3.60.0`, `EventGrid trigger extension 3.6.0`,
  `Azure.Messaging.EventGrid` bumped `4.25.0 → 4.29.0`.
- `docs/testing.md` — `StateWriterFunctionTests` inventory added, total 6 → 9.
- `docs/adr/0002-cosmos-serverless.md` — partition key rationale section added
  (why `routeShortName`, why not `vehicleId` or `routeId`, hot-partition tradeoff).

Non-obvious decisions:

- `CosmosClient` uses `DefaultAzureCredential` — no connection string. Endpoint
  (`Cosmos__AccountEndpoint`) is not a secret; auth is credential-based.
  Locally: add `Cosmos__AccountEndpoint` to `local.settings.json` manually.
  In Azure: plain app setting in compute.bicep (not a Key Vault reference).
  Full CD wiring to be verified at SP1-12.
- Stale-write guard costs 1 extra RU per invocation (the read). Acceptable to
  prevent out-of-order writes from Event Grid's at-least-once delivery guarantee.
- `[SignalROutput]` with `null` return skips the broadcast silently — used for
  stale events where no frontend update should be sent.

## SP1-07 — Alerter chain ✅

Closed 2026-05-30. PR #4 squash-merged. All 12 unit tests pass (9 existing + 3 new).

Landed:

- `SydneyPulse.Core/Cosmos/AlertDocument.cs` — Cosmos document model for the
  `alerts` container. Partition key `routeShortName`, document id `alertId`
  (one doc per alert, upsert overwrites on repeat delivery). TTL 24 h set by
  container policy — no code required to expire alerts.
- `SydneyPulse.Functions/FunctionConstants.cs` — shared `internal static class`
  for all Azure infrastructure string constants (Cosmos, Service Bus, SignalR).
  Service-explicit naming convention: `VehiclesSignalRHub`, `AlertsCosmosContainer`,
  `AlertsServiceBusTopic`, etc. `InternalsVisibleTo("SydneyPulse.Tests")` exposes
  to the test project. No-magic-strings rule documented in `functions/CLAUDE.md`.
- `SydneyPulse.Functions/Functions/AlerterFunction.cs` — `ServiceBusTrigger` on
  `sydney-pulse-alerts / alerter-sub`. Unwraps CloudEvent envelope (Event Grid
  delivers the full JSON envelope to Service Bus, not just the payload). Upserts
  `AlertDocument` to Cosmos `alerts` container. Broadcasts `alertReceived` to
  SignalR `alerts` group.
- `StateWriterFunction.cs` refactored to use `FunctionConstants` — all magic
  strings removed from both the function and its tests.
- `SydneyPulse.Tests/Unit/AlerterFunctionTests.cs` — 3 unit tests: valid alert
  upserts and broadcasts, CloudEvent missing data returns null, nullable
  `StartsAt`/`EndsAt` fields map correctly.
- NuGet: `Microsoft.Azure.Functions.Worker.Extensions.ServiceBus 5.22.0`.
- `docs/testing.md` — `AlerterFunctionTests` inventory added, total 9 → 12.

Non-obvious decisions:

- Event Grid delivers the full CloudEvent JSON envelope to Service Bus (not just
  the `data` payload). `AlerterFunction` uses `CloudEvent.ParseMany` to unwrap
  the envelope, then `JsonSerializer.Deserialize` with `PropertyNameCaseInsensitive
  = true` to handle the Azure SDK's camelCase serialisation of `ServiceAlert`.
  `BinaryData.ToObjectFromJson<T>()` did not handle the casing round-trip correctly.
- No stale-write guard on alerts (unlike vehicles). Alert upserts are idempotent
  by `alertId` — repeat Event Grid delivery safely overwrites with the same data.
- `HubName = "alerts"` is a separate SignalR hub from `"vehicles"`. Clients need
  two connections at the Free SKU (20 total). NegotiateFunction still targets
  `"spike"` from SP1-02 — will be updated in SP1-08.
- `FunctionConstants` is `internal` — `InternalsVisibleTo` required to use it in
  the test project rather than making it `public`.

## SP1-08 — HTTP API ✅

Closed 2026-05-31. PR #5 squash-merged. All 19 unit tests pass (12 existing + 7 new).

Landed:

- `SydneyPulse.Functions/Functions/VehiclesFunction.cs` — `GET /api/vehicles`.
  5-second in-process `IMemoryCache` keyed by full query string. Optional
  `?mode=` and `?routeShortName=` query parameters (partition-scoped Cosmos
  query when `routeShortName` is present, mode-filtered full scan otherwise).
  `Cache-Control: public, max-age=5` header for CDN/browser caching.
- `SydneyPulse.Functions/Functions/AlertsFunction.cs` — `GET /api/alerts`.
  Cross-partition Cosmos query `ORDER BY c.receivedAt DESC`.
- `SydneyPulse.Functions/Functions/RoutesFunction.cs` — `GET /api/routes`.
  Reads from `TfNswFeedClient.GetRoutesAsync()` — reuses the 1-hour in-memory
  GTFS static cache; zero Cosmos RUs per call.
- `SydneyPulse.Functions/Functions/NegotiateFunction.cs` — updated `POST /api/negotiate`.
  Replaced hardcoded `"spike"` hub with two `[SignalRConnectionInfoInput]` bindings
  (`VehiclesSignalRHub` and `AlertsSignalRHub`). Runtime selects via `?hub=` query param.
- `SydneyPulse.Core/Cosmos/VehicleDocument.cs` — extended with 4 denormalized fields:
  `RouteLongName`, `RouteColor`, `Mode`, `OccupancyStatus` (ADR-0011).
- `SydneyPulse.Functions/Functions/StateWriterFunction.cs` — maps the 4 new
  `VehicleDocument` fields from the incoming `VehicleUpdate` event.
- `SydneyPulse.Functions/Program.cs` — `services.AddMemoryCache()` added for VehiclesFunction.
- `docs/adr/0011-denormalized-vehicle-document.md` — documents the decision to
  store route metadata (mode, long name, colour, occupancy) in the vehicle document
  at write time rather than joining at query time.
- `docs/api.md` — corrected `VehicleUpdate.v1` event schema (added `routeLongName`,
  `routeColor`, `mode`); added `?hub=` param to negotiate endpoint; aligned vehicles
  response to ADR-0011 shape.
- `docs/testing.md` — filter examples fixed (`ClassName=` → `FullyQualifiedName~`
  across all existing entries); inventory sections and filter lines added for
  `VehiclesFunctionTests`, `AlertsFunctionTests`, `RoutesFunctionTests`; total count
  updated 12 → 19.
- `SydneyPulse.Tests/Unit/VehiclesFunctionTests.cs` — 3 tests: unfiltered 200 + Cache-Control
  header, mode filter sends WHERE clause, routeShortName filter uses PartitionKey.
  Contains `TestHttpRequestData` / `TestHttpResponseData` test doubles for isolated-worker
  unit tests (HttpCookies is abstract; Headers needs both getter and setter).
- `SydneyPulse.Tests/Unit/AlertsFunctionTests.cs` — 2 tests: WithAlerts returns 200 +
  iterator called once; EmptyContainer returns 200 (not 404).
- `SydneyPulse.Tests/Unit/RoutesFunctionTests.cs` — 2 tests: WithRoutes returns 200 +
  `GetRoutesAsync` called once per mode; EmptyFeed returns 200.

Non-obvious decisions:

- `WriteAsJsonAsync` resolves `IObjectSerializer` from `FunctionContext.InstanceServices`
  at runtime — which is `null` in `Mock.Of<FunctionContext>()`. Switched to
  `JsonSerializer.Serialize + WriteStringAsync` consistently across all HTTP functions.
- `[SignalRConnectionInfoInput]` attribute argument must be a compile-time constant.
  Two bindings declared; runtime selects based on `?hub=` param. Defaults to
  `VehiclesSignalRHub` when param is absent.
- `GetRoutesAsync` returns `IReadOnlyDictionary<string, RouteInfo>` — must call
  `.Values` to iterate routes for the API response.
- `status`, `stopName`, `carriages` from original api.md spec require GTFS-RT
  trip update data not currently fetched — deferred with ADR-0011 reference.

Developer decisions during review:

- Added `?hub=` query param documentation to `POST /api/negotiate` section of api.md.
- Fixed pre-existing `ClassName=` bug in all testing.md filter examples.
- Added explicit `JsonOptions` (CamelCase + WhenWritingNull) to RoutesFunction for
  consistency with VehiclesFunction and AlertsFunction.
- Switched NegotiateFunction from `WriteAsJsonAsync` to `WriteStringAsync` (same
  root issue as AlertsFunction — no host DI in unit tests).

## SP1-15 — Archiver Function ✅

Started 2026-06-04. Closed 2026-06-05. **Shipped via PR #7, squash-merged
as `b04b766`.** 27 files, +3079/-54.

Pipeline is end-to-end functional: Event Grid → `ArchiverIngestFunction`
(JSONL append) → Data Lake `pending/` → `ArchiverFlushFunction` (every 5 min)
→ Parquet + `_manifest.json` in `archive/` → pending blob deleted.

### What landed (13 methods + supporting types)

- **Core abstractions** — `HivePartitionPath` (`ForHour`, `ForFile`, `Parse`),
  `ArchiveEvent` record (24 unified fields), `ArchiveManifest` + `ArchiveManifestFile`,
  `IParquetArchiveWriter` + `ParquetArchiveWriter` (3 row-group write + roundtrip-tested).
- **Function infrastructure** — `IPendingBlobStore` (`GetAppendBlob`,
  `ListPartitionPathsAsync` returning `IAsyncEnumerable<string>`) +
  `PendingBlobStore` impl. Singleton in DI.
- **Function classes** — `ArchiverIngestFunction` (RunAsync + MapToArchiveEvent +
  AppendToPendingAsync), `ArchiverFlushFunction` (RunAsync + ListCloseablePartitions +
  ReadPendingEvents + DedupeByEventId + WriteManifest + FlushPartitionAsync).
- **Constants** — `ArchiveEventsBlobName`, `ArchiveManifestBlobName`, `PendingEventsBlobName`
  on `FunctionConstants` (no magic strings).
- **Tests** — 19 → **55** total tests, 36 new for SP1-15.

### Key design decisions locked in

- **`SourceTimestamp` rename in ADR-0012** — the spec/ADR draft called the primary
  timestamp `vehicleTimestamp`, but the unified schema serves alerts too. Renamed
  to `sourceTimestamp` everywhere.
- **`DateTime` UTC for Parquet timestamp columns** — Parquet.NET 4.x dropped
  `DateTimeOffset` support. `ArchiveEvent` keeps `DateTimeOffset` (domain),
  Parquet schema declares `DataField<DateTime>` (storage). Conversion via
  `.UtcDateTime` / `?.UtcDateTime` in `BuildColumns`.
- **Append-blob + Timer pattern** (rejected Durable Functions Entities).
  Append blob writes are atomic per AppendBlock; crash safety without
  orchestration complexity.
- **Single row group per Parquet file** — our file size is 2–20 MB (well below
  the recommended 128 MB minimum where multiple row groups pay off).
- **Unified flat 24-column schema with camelCase names** — 7 required + 17
  nullable, discriminated by `eventType` + `eventVersion`. One Parquet shape
  serves both VehicleUpdate.v1 and ServiceAlert.v1.
- **`IPendingBlobStore` abstraction** — `BlobContainerClient.GetAppendBlobClient`
  is an SDK extension method (not virtual), so Moq cannot intercept it directly.
  Wrapping it in a one-method interface gave us a clean unit-test seam AND moved
  the container-resolution hop out of every Function ctor. Pattern reference for
  any future Azure SDK abstraction need where extension methods block direct mocking.
- **`IAsyncEnumerable<string>` for partition listing** — streams Hive partition
  prefixes from Azure Storage's paginated `GetBlobsAsync` without buffering the
  whole container in memory. `[EnumeratorCancellation]` on the producer's CT
  parameter so consumer-side `.WithCancellation()` merges with the producer's
  token (otherwise silently ignored — classic gotcha).
- **Read-time dedup, not write-time** — `ReadPendingEvents` returns raw events;
  `DedupeByEventId` is a separate pure helper called from `FlushPartitionAsync`.
  A test pins this boundary so a future "helpful" refactor can't silently fold
  dedup into the I/O path.
- **`_manifest.json` is the commit point** — partition becomes queryable only
  after manifest write succeeds. Pending blob deletion is gated on that.
  Pattern equivalent to Hive's `_SUCCESS`, Iceberg's `metadata.json`, Delta
  Lake's `_delta_log`.
- **`overwrite: true` everywhere on the archive side** — makes re-flush after a
  crash idempotent. Same flag on Parquet upload AND manifest upload. The whole
  crash-safety story pivots on this single boolean.
- **`ArchiveOptions.ArchiveContainer` not `FunctionConstants`** — config plumbing
  is consistent across pending and archive containers.

### Concept primers + interview-prep landed for SP1-15

- `docs/parquet-datalake-primer.md` (gitignored). Sections 1–6 cover Parquet,
  Data Lake Gen2, blob types, Hive partitioning, pipeline, alternatives.
  **Section 7 — Event-driven durability** covers the at-least-once + atomic
  AppendBlock + read-time dedupe-by-EventId triangle with a t₀–t₇ duplicate
  timeline and the EventId-provenance gotcha (verified by reflection probe).
  **Section 8 — Manifest files as data-lake commit marker** covers the four
  problems manifest files solve (atomicity, slow-LIST avoidance, race
  protection, partition pruning) with real-world parallels to Iceberg / Delta /
  Hudi / Hive.
- `docs/interview-prep.md` (gitignored) — new section **SP1-15 — Archiver**
  with Q1 (crash-safety), Q2 (manifest files), Q3 (`IAsyncEnumerable<T>`)
  in point-based story format.
- `SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx` — mechanical 15-bullet model
  answers for the same Q1/Q2/Q3.

### Out of scope (deferred deliberately)

- **Azurite integration test** — would catch the kind of bug we caught manually
  during this PR (`ArchiveManifestBlobName` mis-reference inside `ArchiveManifestFile`).
  Scoped as **SP2-01** (data inspection + observability tooling).
- **Event Grid `archiver-ingest` subscription wiring** — `messaging.bicep`
  declares it conditionally; first-pass deploy passes empty strings, second-pass
  deploy or post-deploy CLI step wires it. Decision deferred to **SP1-12** (CI/CD).

## SP1-16 — Backend visibility (manual deploy + smoke) ✅

Started 2026-06-09. Closed 2026-06-20. Execution plan:
[`backend-manual-deploy-plan.md`](backend-manual-deploy-plan.md).

Phases A–D shipped directly to `main` across multiple commits during the
active smoke + debug period. Phases E + F shipped via PR #8 squash-merged
as `cbe32eb`.

### What landed

**Infra + runtime (Phases A–C)**
- First Function App publish to `sydney-pulse-func-dev` (10 functions discovered)
- Key Vault secrets seeded (`TfNswApiKey`, `AzureSignalRConnectionString`,
  `ServiceBusConnectionString`)
- Event Grid webhook subscriptions wired to the real Function App hostname
  (state-writer + archiver no longer placeholders)
- CORS tightened from `*` → `http://localhost:5500` for the spike client

**End-to-end smoke (Phase D)**
- D.1 Poller ✅ — App Insights confirms 30 s cadence
- D.2 Event Grid ✅ — publish counts > 0
- D.3 StateWriter ✅ — Cosmos `vehicles` populating
- D.4 Alerter chain ✅ — Service Bus topic → SignalR alerts hub end-to-end
- ~~D.5 ArchiverIngest~~ + ~~D.6 ArchiverFlush~~ — descoped to SP-19 (Sprint 2)
- D.7 HTTP API ✅ — `/api/vehicles`, `/api/alerts`, `/api/routes` all 200
  (fixtures captured)
- D.8 SignalR ✅ — `spike-deployed.html` receives `vehicleUpdated` events
  end-to-end

**Debug stories surfaced during D** (writeups in gitignored
`docs/sp1-16-debug-stories.md`):
- #11–#14 — Poller / Service Bus DLQ findings (became the SP-18 Sprint 2
  feature design)
- #15 ★ — Service Bus app-setting key drift (Functions runtime suffix
  vocabulary is `__fullyQualifiedNamespace`, not `__ConnectionString`).
  Fixed via Managed Identity switch in `ff32796`
- #20 ★ — SignalR group-vs-hub silent drop. `GroupName` on broadcast
  filtered to a 0-member group. Fixed by removing `GroupName` from
  StateWriter + Alerter in `6f84c16`. Mirrored to `interview-prep.md`
  Q2 + Word doc

**Documentation (Phases E + F)**
- `docs/runbooks/dev-smoke-evidence.md` — 10 screenshots, 4 KQL queries,
  captured fixtures, ★ SignalR Live Trace section
- `docs/runbooks/manual-deploy-dev.md` — reproducible deploy recipe;
  fresh-RG and code-only paths, 3-layer rollback
- 3 dated HTTP API fixtures under `functions/SydneyPulse.Tests/Fixtures/` —
  reusable by SP1-09 and SP-18

### Non-obvious decisions

- **D.5 / D.6 descoped → SP-19.** Sprint 1 frontend is the Commuter
  Dashboard (HTTP API + SignalR only); archive smoke pairs with Sprint 3
  Analytics view.
- **Direct-to-main commits during D.** Several fixes pushed straight to
  main with admin override during active debug. PR #8 is the formal
  SP1-16 closing artefact; prior commits' context lives in the
  debug-stories doc.
- **`spike-deployed.html` kept in repo root.** Smoke verification
  dependency; not production code, mirrors SP1-02's `spike.html`.

### Out of scope (deferred)

- Phase G plan's 3 quiz Qs (MI/RBAC, KQL, publish gotcha) — folded into
  always-on SP1-14 cadence
- Archiver smoke (D.5 / D.6) → SP-19, Sprint 2

## SP1-09 — Angular scaffolding (deeper) ✅

Started 2026-06-20. Closed 2026-06-24. **Shipped via PR #9, squash-merged
as `fcb4466`.** Jira: SP-9.

### What landed (4 commits → 1 squash)

- **`dae300a` — Angular 18 → 20 upgrade.** Cleared 8 high-sev advisories
  surfaced by `npm audit`; closed the SP-20 tech-debt row in-sprint with
  zero app-code changes (framework-only).
- **`474c0ae` — Service layer + env config + 3 HTTP services.**
  `VehiclesService`, `AlertsService`, `RoutesService` against the deployed
  dev API. `environment.ts` / `environment.prod.ts` swap via `angular.json`
  `fileReplacements`. Models derived from captured
  `Fixtures/*-2026-06-17.json` so backend/frontend drift surfaces as a
  type error at the import site.
- **`f28ba19` — RealtimeService crash-safety fix + FE tests deferred.**
  `connect()` builds locally → publishes refs only after BOTH hub starts
  succeed (prevents leaked Free-SKU connection slots on partial failure).
  `disconnect()` uses `Promise.allSettled` so a stop() failure can't
  strand the refs. Frontend unit tests pulled out to [SP-21](https://gsoft85512.atlassian.net/browse/SP-21);
  `app.component.spec.ts` retained so `ng test` infrastructure is intact.
- **`45958a4` — App shell + 4 lazy routes + nav.** `RouterOutlet` + nav
  linking to `landing`, `live`, `analytics`, `ops` — all `loadComponent`
  so initial bundle stays small.

### Key design decisions locked in

- **A — Mixed envelope/array return.** Services drop the envelope where it
  carries no extra fields (`AlertsService`, `RoutesService` → `T[]`), keep
  the envelope where it carries timing data (`VehiclesService` →
  `VehiclesResponse` so the live view can render stale-data UI from
  `feedTimestamp`).
- **B — Two `HubConnection`s, one service.** Mirrors the backend's two
  separate hubs (`vehicles`, `alerts`). Hub names + event names live in
  `signalr-events.constants.ts`, mirrored from `FunctionConstants.cs`
  (no-magic-strings rule). Drift = silent dropped messages on the client
  — Debug Story #20 ★ cluster precedent.
- **C — `inject()` over ctor injection** throughout. Modern Angular 18+
  idiom; lighter syntax for standalone services.
- **Raw Leaflet + RxJS/Signals (no NgRx).** Confirmed at sprint scope per
  `project_sp109_angular_decisions` memory; no library beyond `leaflet`
  + `@microsoft/signalr` added.

### Non-obvious decisions

- **Dev Function App CORS amended.** `http://localhost:4200` added to the
  dev Function App allow-list in this sprint item (SP1-16 had it at
  `http://localhost:5500` only for `spike-deployed.html`). `ng serve`
  against the deployed dev API now works without proxy.
- **Backend tests stayed green at 64 across the upgrade** — no API
  surface change, services are pure HttpClient wrappers.
- **`Route` interface named `TransportRoute`** to avoid collision with
  Angular Router's `Route` type.

### Out of scope (deferred)

- Frontend unit tests → [SP-21](https://gsoft85512.atlassian.net/browse/SP-21).
  Rationale captured in PR #9 + `web/CLAUDE.md` Testing section.
- Real header/footer styling → SP1-11. Nav is functional but bare.
- Leaflet map wiring → SP1-10. `live.component` is a placeholder.

## SP1-14 — Code review + interview-prep quiz (always-on) 🔄

Started 2026-06-01. Jira: SP-15. Reframed 2026-06-03 from a 2-day discrete
item to a sprint-long always-on discipline. See `CLAUDE.md` "Daily rhythm"
section for the full framework. Continues as SP2-10 / SP3-9 / SP4-10 / SP5-9
in future sprints. Word-doc artefact at
`C:\BUDDHIKA\SydPulse-P6\SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx`
accumulates across sprints.

### Process

The developer reads each file group from `reading-plan.xlsx` independently,
then signals readiness. Claude quizzes the developer on that group via
open-ended questions (no looking at code). After each group:

- Claude documents questions + model answers into a Word document at
  `C:\BUDDHIKA\SydPulse-P6\SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx`.
- Jira SP-15 and this file are updated with progress.

No coding work in this item — read, understand, quiz, document.

### Progress

| File group | Files covered | Quiz status |
|---|---|---|
| 1 — Data Contracts (events) | `VehicleUpdate.cs`, `ServiceAlert.cs` | ✅ quizzed |
| 1 — Data Contracts (Cosmos) | `VehicleDocument.cs`, `AlertDocument.cs` | ✅ quizzed |
| 1 — Data Contracts (constants) | `FunctionConstants.cs` | ✅ quizzed |
| 2 — TfNSW Client | `TfNswOptions.cs`, `ITfNswFeedClient.cs`, `TfNswFeedClient.cs` | ✅ quizzed |
| 3 — Event Pipeline | `PollerFunction.cs`, `StateWriterFunction.cs`, `AlerterFunction.cs` | 🔄 PollerFunction ✅ (Q1–Q6 + 1 follow-up); StateWriterFunction 🔄 (Q1–Q2 answered + reviewed 2026-06-04 AM, Q3 stale-write guard pending, Q4–Q6 pending); AlerterFunction ⬜ pending |
| 4 — HTTP API | `VehiclesFunction.cs`, `AlertsFunction.cs`, `RoutesFunction.cs`, `NegotiateFunction.cs` | ⬜ pending |
| 5 — DI Wiring | `Program.cs`, `EventGridOptions.cs`, `CosmosOptions.cs` | ⬜ pending |
| 6 — Tests | All test files | ⬜ pending |
| 7 — Bicep | `main.bicep`, `messaging.bicep`, `compute.bicep`, `data.bicep` | ⬜ pending |

### Word document

Questions and model answers accumulate in:
`C:\BUDDHIKA\SydPulse-P6\SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx`

Sections added so far:
- SP1-14: VehicleUpdate & ServiceAlert Event Records (6 questions)
- SP1-14: VehicleDocument & AlertDocument (6 questions)
- SP1-14: FunctionConstants (6 questions)
- SP1-14: TfNSW Client (TfNswOptions, ITfNswFeedClient, TfNswFeedClient) (6 questions)
- SP1-14: PollerFunction — all 6 questions + 1 follow-up (local dev / Event Grid emulator)

### Story-flavoured answers (`docs/interview-prep.md`)

Story-mode counterparts to the Word doc per the two-doc system (CLAUDE.md
"Daily rhythm"). File is gitignored — local reference only.

Debug-story / scenario Qs landed:
- SP1-15 — Archiver: Q1 (crash-safety) + Q2 (manifest files) + Q3 (`IAsyncEnumerable<T>`) — 2026-06-05
- SP1-16 — Q1 (zombie freshness) + Q2 (SignalR group-vs-hub diagnostic) — 2026-06-19
- SP1-16 — Q3 (defending hub-wide vs groups, PM-pushback scenario) — 2026-06-21

### Verbal-recall sessions

- 2026-06-21 — Debug Story #20 (SignalR group-vs-hub): Q1 diagnostic-order
  walked end-to-end (strong on chain-of-proof + Live Trace as the
  asymmetry-finding tool); Q3 design-defence calibrated up from
  junior-YAGNI to senior-triangle (numbers + alternative + reversal
  conditions). Q3 captured back to `interview-prep.md` SP1-16 section
  with PM-pushback scenario in the question header.

## SP1-12 — GitHub Actions CI/CD ✅

Started 2026-06-25. Closed 2026-06-29. **Shipped via PR #10, squash-merged
as `6256c39`.** Jira: SP-12. Carried across two sessions: 2026-06-25
scaffolded + walked 5 of 7 files; 2026-06-29 dropped the walkthrough
(per new "no dev-time Q&A capture" rule), bootstrapped Azure OIDC,
opened the PR, debugged 5 distinct gotchas, and shipped.

### What landed

- **`ci.yml`** — PR merge gate. `pull_request` trigger to `main`. Calls
  `_dotnet-lint-test.yml` with `runWhatIf: true`. No Azure mutations —
  pure validation.
- **`deploy-dev.yml`** — full pipeline. Triggers: `push: branches: [main]`
  (auto-deploy) + `workflow_dispatch` (manual re-run for incident drills).
  Sequential job graph: `lint-test` → `deploy-infra` → `publish-app` with
  `needs`-based gating. `concurrency: deploy-dev` group with
  `cancel-in-progress: false` so stacked pushes serialise instead of
  killing the running deploy.
- **3 reusable workflows** at the top of `.github/workflows/` with `_`
  prefix (forced by the GitHub validator — reusables can't live in a
  subdirectory; see Debug Story #20):
  - `_dotnet-lint-test.yml` — format check → restore → build → test → bicep
    build → optional OIDC login + `bicep what-if`.
  - `_bicep-deploy.yml` — OIDC `azure/login` → `az deployment group create`.
  - `_func-publish.yml` — `dotnet publish` → zip → OIDC login → zip-deploy →
    smoke (Function App state == Running).
- **OIDC federated identity** to Azure — no client secret stored anywhere.
  Three federated credentials on `sp-github-actions-dev` app registration
  (branch=main, pull_request, environment=dev), scoped to
  `repo:buddhika85/Sydney-Pulse:...`. RBAC: Contributor + User Access
  Administrator on **both** `sydney-pulse-rg-dev` AND `DevPulseRG` (cross-RG
  Bicep needs roles on every RG it touches — see Debug Story #22).
- **Environment protection** on GitHub `dev` environment with
  deployment-branch rule restricting to `main` only.
- **Two OIDC bootstrap runbooks** — `docs/runbooks/github-actions-oidc-setup.md`
  (CLI) and `docs/runbooks/github-actions-oidc-setup-portal.md`
  (Portal companion). Portal version walked end-to-end this sprint item.
- **README.md** in `.github/workflows/` — file-layout map, per-workflow
  description, **OIDC handshake explainer with ASCII diagram** (4 actors,
  2 tokens), repo-secret list, branch-protection recommendations.

### Pipeline proven end-to-end

First real automated deploy via `workflow_dispatch` from feature branch on
2026-06-29 (after temporarily widening the dev environment allow-list):
**5 minutes from dispatch to deployed**. Then the squash-merge to `main`
auto-fired `deploy-dev.yml` via `push: branches: [main]` — green deploy
on `6256c39`.

| Job | Time | Result |
|---|---|---|
| `lint-test` | 1m 22s | ✅ format + build + test + bicep build + what-if |
| `deploy-infra` | 2m 17s | ✅ Bicep deploy to `sydney-pulse-rg-dev` + cross-RG topic to `DevPulseRG` |
| `publish-app` | 1m 17s | ✅ Function App publish + smoke (state=Running) |

### Five debug stories surfaced during the cycle

Full writeups in gitignored `docs/sp1-12-debug-stories.md`:

- **#20 ★** — Reusable workflows can't live in `.github/workflows/reusable/`.
  GitHub validator rejects nested `uses:` paths; surfaces only at PR-open
  time, not at commit time.
- **#21** — `dotnet format` drift accumulated invisibly across 15+ sprint
  items because no pre-commit hook enforced format. First CI lint pass
  found 21 files with whitespace violations.
- **#22 ★** — Cross-RG Bicep needs RBAC on every RG it touches. ADR-0003
  reuse pattern (DevPulseRG Service Bus namespace) has a hidden CD shadow
  — every consumer needs explicit RBAC on the reused RG.
- **#23** — GitHub UI `workflow_dispatch` button only appears after the
  workflow file exists on the default branch. CLI `gh workflow run` bypasses.
- **#24 ★** — Environment branch protection blocks pre-merge deploy
  validation. Two-edged tool — protection you want in steady state
  actively blocks the testing you need before steady state.

Cross-cutting takeaway: **first run of automated CI/CD against real Azure
surfaced 5 distinct gotchas across 5 completely different surfaces** (YAML
validator, code drift, cross-RG RBAC, UI affordance, protection rule) —
none caught by unit tests, dev-time builds, or the manual deploy recipe.
That's the case for CI/CD being its own discipline, not a packaging of a
working manual recipe.

### Non-obvious decisions

- **`_` prefix on reusables** — chosen over a flat name to keep the
  visual grouping the original `reusable/` subdir was meant to provide.
  Sorts to the top of the Actions UI alphabetically + marks them as
  "internal, not directly triggerable."
- **What-if step always runs on PR (`runWhatIf: true`)** — shows the
  Bicep delta in the PR's check log so reviewers can see infra changes
  without leaving GitHub. Skipped on `deploy-dev.yml`'s pre-deploy
  gate (`runWhatIf: false`) because the what-if already ran on the PR.
- **OIDC condition on UAA: "Allow user to assign all roles"** — simplest
  dev-tier choice. Sprint 2 prod hardening will constrain to just the
  roles `infra/modules/role-assignments.bicep` needs.
- **Separate prod app registration (Sprint 2)** — not a fourth federated
  credential on the dev app reg. Blast-radius isolation: a dev-side
  mistake cannot reach prod.

### Out of scope (deferred)

- **Prod CI/CD** (`deploy-prod.yml`, prod app registration, slot swap step)
  → Sprint 2. Section "Adding prod" in the OIDC runbook + `.github/workflows/README.md`
  document the pattern.
- **Branch protection main rule with `lint-test` required check** → can be
  added now via Settings → Branches → main, since `lint-test / lint-test`
  is a stable check name. Pending until Sprint 2 hardening.

## SP1-10 — Live dashboard ✅

Closed 2026-07-10 via PR #11 (`f10a434`). Jira: SP-10.

**Ambitious scope, ambitious delivery.** What started as a chip refactor plus
a 3-step usability pass grew — during a two-day smoke-test marathon
(2026-07-08 → 07-10) — into a Sprint 1 close-quality delivery: the dashboard
shipped complete, **plus 4 senior-grade debug stories with ★ interview
soundbites**. The documentation cluster (mechanical writeups + interview
questions + 14 embedded screenshots + written ADR amendment) is arguably the
strongest single-sprint-item portfolio evidence in the project so far.

### ADRs landed during Phase 1 planning (2026-06-30)

- **ADR-0013** — Trust the SignalR stream on the live dashboard; client-side
  TTL prune (5 min, aligned to Cosmos `vehicles` container TTL) replaces a
  periodic-refetch hedge.
- **ADR-0014** — Raw Leaflet with Angular lifecycle; no `ngx-leaflet`
  wrapper. Formalizes the SP1-09 decision for the public record.

### Dashboard core (from original scope)

- Leaflet map with `L.circleMarker` markers, upsert keyed by `vehicleId`,
  5-min TTL client-side prune (ADR-0013).
- SignalR initial-snapshot-then-stream, no periodic HTTP refetch.
- Route filter chip strip refactored from `<select>` dropdown to
  colour-coded chip buttons — filter + legend as one UI element.
- Alerts panel as presentational right-rail with route-scoped filter.
- Freshness pill driven by `max(feedTimestamp, latestStreamTs)` — 60s stale
  threshold, 5s re-eval interval.
- Frontend environment isolation flags (`enableSignalRRealtime`,
  `enableFreshnessTimer`) added to support Phase A / B / C smoke isolation.

### Chip usability pass (3 steps, layered feature)

- **Step 1** — chip hover count tooltips: per-route `"N vehicles, M alerts"`
  via native `[attr.title]`; "All" chip aggregate tooltip via pass-through
  inputs (`totalVehicleCount`, `totalAlertCount`).
- **Step 2** — mode chip filter-aware vehicle count: `SYDNEY TRAINS · N VEHICLES`
  unfiltered / `X OF N VEHICLES` when a route chip is active. Middle-dot
  separator.
- **Step 3** — alerts panel filter-aware header count: `ACTIVE ALERTS (N)` /
  `ACTIVE ALERTS (X OF N)`. Also a silent cleanup — `LiveComponent.filteredAlerts`
  computed deleted; `AlertsPanelComponent.visibleAlerts` becomes sole owner
  of the alerts filter (double-filter path removed).

### 4 debug stories discovered + fixed during SP1-10 smoke testing

- **Story #6** — `routeColor` double-hash silent visual bug in
  `vehicle-marker.ts`. All markers rendering Leaflet default blue because
  frontend prepended `#` to a value the backend already sent with `#`.
  Contract-lock JSDoc on `Vehicle.routeColor` prevents recurrence.
- **Story #7 ★** — SignalR camelCase drift. Worker-wide JSON serializer
  configured in `Program.cs`. Previously the "camelCase on the wire"
  contract was implemented in **4 places** (3 HTTP endpoints + Cosmos
  client) but the SignalR output binding inherited the worker default
  (PascalCase). Class-of-bug fix: central policy in one line. Deployed
  via `workflow_dispatch` on the feature branch with a controlled 11-min
  env-protection-rule window.
- **Story #8 ★** — Alert stream unbounded growth. ADR-0010 explicitly
  said the frontend owns dedup by `alertId`; `wireAlertStream` just
  prepended without dedup — **a written ADR contract the code ignored
  for weeks.** Fix: upsert-by-`alertId` with `receivedAt` comparison
  matching the ADR-0010 wording. Signal-reference-equality lesson also
  captured as a `LEARN:` comment in the code.
- **Story #9 ★** — Freshness pill stuck stale. `StateWriterFunction`
  broadcast the `VehicleUpdate` event shape instead of `VehicleDocument`
  Cosmos-doc shape (unlike `AlerterFunction` which broadcasts
  `AlertDocument`). Frontend `Vehicle.ts` was written for the doc shape,
  so stream fields (`timestamp`, `id`, `updatedAt`) silently dropped —
  freshness pipeline never advanced. Fix: broadcast `vehicleDocument`,
  matching the alerts-hub pattern. Same class-of-bug as #7 but
  DTO/shape convention rather than casing convention.
- **Story #10 ★** — Cosmos cross-partition dupes. Same `alertId`
  legitimately exists under multiple `routeShortName` partitions
  (multi-route TfNSW alerts). **Unmasked by Story #7 fix** — before
  that, PascalCase drift made `alert.alertId` undefined, so the `$index`
  fallback in the `@for` track expression masked the collision. Bug
  archaeology in real time. Fix: composite track key
  `(alertId, routeShortName)` in the template + composite dedup
  predicate in `wireAlertStream`. First-instinct Poller-side dedup was
  proposed then rejected on architectural grounds — *the write layer's
  job is to preserve source data; the presentation layer's job is to
  make sense of it.*

### Doc updates delivered

- **ADR-0010 amendment** — composite dedup key clarification. Cosmos
  uniqueness is per-partition, not global; frontend dedups by
  `(alertId, routeShortName)`.
- **`justify_sb_usage.md`** — interview soundbite refreshed to composite
  key semantics.
- **`docs/sp1-10-debug-stories.md`** — Stories #6, #7, #8, #9, #10 all
  fully written with 14 embedded screenshots (gitignored under
  `docs/images/*-story-*.png`).
- **`docs/interview-prep.md`** — 12 ★ interview questions across the
  debug-story cluster + 4 chip-refactor interview questions.

### Testing

- **`Testing/Testing.xlsx` — 17/17 rows Pass** (2026-07-10 final smoke).
- Backend `dotnet test` — **64/64 tests passing**.
- Frontend unit tests remain deferred to SP-21 per SP1-09 decision.

### Deferrals + Sprint 2 tech debt captured

- **Angular SWA deploy workflow** — deferred to SP1-13 (sprint wrap)
  which owns the live URL deliverable. Needs new `_swa-publish.yml`
  reusable workflow + job in `deploy-dev.yml` + `AZURE_STATIC_WEB_APPS_API_TOKEN`
  GitHub Actions secret.
- **`VehicleWireDto` refactor** — captured in `StateWriterFunction.cs`
  code comment. Would decouple wire from storage cleanly if that
  trade-off is ever worth pursuing honestly (Sprint 2 candidate).
- **Bankstown line route catalogue gap** — BNK_1a / BNK_1c chip labels
  fall back to raw TfNSW routeIds because the static route catalogue
  doesn't have entries for them. Small, separate finding.

### Interview evidence produced

Debug story cluster is now the highest-density senior-grade interview
material in the project. Standout narrative angles:

- *"Consistency across analogous paths beats theoretical decoupling"*
  (Story #9)
- *"Data completeness > premature normalization at write layer"*
  (Story #10)
- *"5 consumers, 4 enforced, 1 silent bug"* (Story #7 class-of-bug)
- *"A gate that never fires is an unproven gate"* (Story #7 DevOps)
- *"Trust source data, not intermediary formatters"* (Story #6 silent
  visual bug)
- *"A written ADR is not enforcement"* (Story #8 ADR-based dedup)

## SP1-11 — Landing page ✅

Started 2026-07-10. Closed 2026-07-11 via PR #12 (`29dfccc`). Jira: SP-11.
~3 hours across two sessions.

### What landed

- **6-section landing page at `/`** — hero + CTA, 3-card feature strip,
  hand-authored architecture SVG, tech stack chips, portfolio note, footer.
  Standalone + OnPush per `web/CLAUDE.md`.
- **Architecture SVG mirrors real ADR-0001 topology** — Service Bus
  subscription-filter tier added between Event Grid and Alerter; StateWriter
  and Alerter both dual-write via a "Persist + Broadcast" manifold pill so
  the four converging writes render without crossing arrows. Colour palette
  mirrors `docs/diagrams.md` legend.
- **Tech stack chips lead with senior .NET/Azure signal** — .NET 8 + Azure
  Functions + Managed Identity → data + messaging → App Insights → Bicep +
  GitHub Actions + xUnit → Angular tail (14 chips total).
- **Portfolio note owns the six-year prod experience explicitly** — "the
  same event-driven pattern I've shipped in production for six years"
  removes ambiguity between this project's lifetime and the pattern's lineage.
- **Mobile-responsive tuning** — media query below the `sm` breakpoint bumps
  SVG-internal font sizes so titles/captions stay legible at 375px viewport;
  desktop rendering unchanged.

### Testing

- `ng build` clean; landing chunk 14.80 kB.
- Manual walkthrough at desktop (1440px) and mobile (375px) — 6 sections
  render, no horizontal overflow, external links open in new tabs with
  `rel="noopener noreferrer"`.
- Backend `dotnet test` — 64/64 passing (unchanged, no backend files touched).
- Frontend unit tests remain deferred to SP-21.

## SP1-13 — ✅ Closed 2026-07-16

**Sprint 1 shipped.** Live URL healthy at
https://proud-grass-020b12300.7.azurestaticapps.net/. Two GitHub
releases published: `v0.1.0` (2026-07-14, PR #13 merge + wrap) and
`v0.1.1` (2026-07-15, evidence page + Loom scaffold). Full pipeline
poll → Event Grid → Cosmos + Service Bus → Alerter → SignalR → live
Angular dashboard running against real Sydney Trains data.

### What landed in PR #13

- `_swa-publish.yml` reusable workflow — mirrors `_func-publish.yml`
  shape; runs `ng build --configuration production` then
  `Azure/static-web-apps-deploy@v1`. Wired into `deploy-dev.yml` as
  a `publish-web` job parallel to `publish-app` (both fan out from
  `deploy-infra` — saves ~1 min per deploy).
- Node 24 runtime bumps — `actions/checkout` v4→v5,
  `actions/setup-dotnet` v4→v5, `actions/setup-node` v4→v5,
  `azure/login` v2→v3 across all four reusable workflows. Clears the
  Node.js 20 deprecation warning on every run.
- SWA `skip_app_build` semantics fix — when true, the action treats
  `app_location` as the artifact folder (not source) and IGNORES
  `output_location`. Fix concatenates `webProject + outputLocation`
  into `app_location`. Debug story write-up pending on `main`.
- `environment.prod.ts` API URL — was pointing at
  `sydney-pulse-func-prod` (never provisioned; Sprint 2 concern).
  Repointed at `sydney-pulse-func-dev` with an intent comment.
- **Pulse animation on vehicle markers** — feature-flagged via
  `environment.features.pulseMarkers`. CircleMarker radius scales
  100→130→100 over 450 ms per SignalR update via a triangle wave in
  `requestAnimationFrame`. Handles tracked in module-level `WeakMap`
  so `pruneStale`-removed markers self-clean. Bulk-snapshot path
  deliberately does NOT pulse (100+ markers at page load = visual
  noise, not signal).
- Mobile `/live` layout fix — pre-existing SP1-10 bug: 3 grid areas
  declared but only 2 rows, alerts panel auto-sized off text content,
  map's `1fr` collapsed to 0 at 375 px viewport. Fixed with explicit
  3-row template + mobile media queries (map `min-height: 50vh`,
  alerts `max-height: 40vh` scrollable). Caught during pulse testing,
  fixed in a distinct commit so pulse stayed independently revertable.
- SPA route fallback — `web/public/staticwebapp.config.json` with
  `navigationFallback` so direct URL entry (`.../live`) resolves via
  Angular Router instead of hitting an SWA 404.
- CORS tightening — `compute.bicep` + `frontend.bicep` moved from
  `allowedOrigins: ['*']` to specific origins (dev SWA + `localhost:4200`).
  SWA hostname wired via `frontend.outputs.swaDefaultHostname` into a
  new `webAppOrigin` param on `compute.bicep`, so an SWA re-provision
  doesn't silently break CORS. SignalR uses the local
  `swa.properties.defaultHostname` reference (same-module — avoids
  the circular "consume own output" problem).

### Wrap-up shipped after PR #13 (2026-07-14 → 07-16)

- **README refresh** — portfolio-facing landing with inline Mermaid
  architecture, CI/CD section, ADR cross-references, sprint roadmap,
  plus hero + CI/CD screenshots (`fdaf6c8`).
- **Tag + Release `v0.1.0`** — pushed 2026-07-14, GitHub Release
  published via web UI with full markdown notes.
- **Evidence page** — Angular component at `/evidence` with 8
  captioned sections, mobile-responsive layout, Loom iframe slot
  (env-flagged), and `docs/evidence.md` markdown mirror. All 9
  screenshots captured — served from `web/public/evidence/` for the
  Angular route and mirrored under `docs/images/` for the GitHub
  markdown page.
- **Loom walkthrough recorded + uploaded** — silent-camera video ID
  `7726a3e69ec84db68a86a0290c46bf62` embedded on evidence page.
  Narrated re-record deferred.
- **Tag + Release `v0.1.1`** — 2026-07-15, bundled evidence page +
  Loom scaffold.
- **DomSanitizer fix + evidence config centralisation**
  (2026-07-16, `20518d4`) — Angular was silently stripping the Loom
  iframe `src` binding via `SecurityContext.RESOURCE_URL`
  sanitization; fix wraps the URL with
  `bypassSecurityTrustResourceUrl()`. Same commit moves evidence page
  URLs, release metadata, and Loom video ID into
  `environment.evidence.*` so dev vs prod values live in config, not
  component code. Both env files now shape-symmetric.
- **Debug stories (gitignored, in `docs/sp1-13-debug-stories.md`)** —
  - **#1** — SWA `skip_app_build` semantics: when true, the action
    reinterprets `app_location` as the artifact folder and ignores
    `output_location`. Buried in prose docs, not in `action.yml`.
  - **#2 ★** — Angular `DomSanitizer` silently blanks `iframe[src]`
    bound to a plain string. Trust boundary is provenance of the URL,
    not identity of the domain. Blog-worthy Angular-security teaching
    moment; textbook interview material.

### Deferred out of SP1-13 (post-Sprint-1 backlog)

Not blocking Sprint 1 close; captured in memory + handoff for
follow-up:

1. **CV surgical pass** — swap placeholder URLs for real live URL,
   replace TfNSW-quota proxies with measured p99 from App Insights /
   Cosmos / SignalR diagnostics. Gates any CV send. See memory
   `project_sp1_close_cv_followups`.
2. **Narrated Loom re-record** — silent-camera walkthrough already
   uploaded; narrated version deferred until Week 1 study block +
   first CV batch. Script draft in memory
   `project_post_sprint1_narrated_loom_linkedin`.
3. **LinkedIn post** — needs narrated Loom URL to embed.

## Housekeeping — 2026-06-03 — AzFunctions folder restructure

Source and test layouts reorganised to group Azure Functions by purpose.
Pure refactor — no functional change, no Azure topology change, no Bicep
change.

### What changed

- `SydneyPulse.Functions/Functions/` → `SydneyPulse.Functions/AzFunctions/`
  with three sub-folders:
  - `EventPipeline/` — `PollerFunction`, `StateWriterFunction`,
    `AlerterFunction`
  - `HttpApi/` — `VehiclesFunction`, `AlertsFunction`, `RoutesFunction`,
    `NegotiateFunction`
  - `Spikes/` — `SpikeFunction` (kept for SP1-02 reference, not production)
- All 8 source-file namespaces updated to match
  (`SydneyPulse.Functions.AzFunctions.<group>`).
- Test layout mirrors source: `SydneyPulse.Tests/Unit/AzFunctions/
  {EventPipeline,HttpApi}/` with namespaces updated to match.
  `TfNswFeedClientTests.cs` stays directly under `Tests/Unit/` — it's a
  Core test, not an Az Function test.
- Old `Functions/` folder removed.
- Docs updated: root `CLAUDE.md` example path, `functions/CLAUDE.md`
  solution-layout block + "Add a new Function" guidance, `docs/diagrams.md`
  "What lives where" table and Mermaid node labels, `docs/testing.md` test
  file paths.

### What did NOT change

- Function names (`Poller`, `StateWriter`, `Alerter`, `Vehicles`, `Alerts`,
  `Routes`, `negotiate`, `spike`) — Azure runtime discovers by attribute,
  not class location.
- HTTP routes, Event Grid subscriptions, Service Bus subscription wiring,
  SignalR hub bindings — all attribute-bound and unchanged.
- Any Bicep file. No infra impact at all.
- `.github/` workflows.
- Historical sprint entries (SP1-05/06/07/08) — left referencing the old
  paths since they record what landed at the time of each PR close.

### Verification

- `dotnet build` — 0 warnings, 0 errors.
- `dotnet test` — 19/19 pass.
- `git mv` used for every file → history preserved.

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
- **2026-06-02 — SP1-15 (Archiver Function) added to Sprint 1 scope.**
  Infrastructure (Data Lake container + EG `archiver` subscription) was
  provisioned in SP1-03; the Function App code itself was an open gap not
  scheduled in any sprint. SP1-15 slots between SP1-08 (done) and SP1-09.
  Scope per `sprint-1.md` row 15: Parquet writer + Durable Functions
  batching (5 min / 10K events) + Hive partition layout + ML-ready schema
  (3 timestamps, explicit columns, `_manifest.json` per hour) + Bicep
  storage lifecycle policy (Hot→Cool 30 d, Cool→Cold 90 d) + new ADR-0012
  locking in archive-as-ML-feature-store design for future Sprint 5
  (KQL anomaly detection + ONNX-in-Function predictor). 2-day estimate.
  Sprint total bumped 12 → 14 days.

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

## Next session handoff (2026-07-16 — Sprint 1 CLOSED, pivoting to interview prep)

**Sprint 1 done.** Full event-driven pipeline running end-to-end
against real TfNSW data: Poller → Event Grid → Cosmos + Service Bus →
Alerter → SignalR → live Angular dashboard, at
https://proud-grass-020b12300.7.azurestaticapps.net/. Two releases
(`v0.1.0` on 2026-07-14, `v0.1.1` on 2026-07-15) published to GitHub.
Evidence page live on both the Angular route (`/evidence`) and the
markdown mirror (`docs/evidence.md`), with all 9 screenshots captured.

Per memory `project_post_sprint1_full_time_prep`, Sprint 2 is now
paused. Top of the stack is interview prep — Q-Bank study plan +
Advanced C# brush-up + System Design + CV surgical pass.

### Quick state snapshot

- On `main` at `20518d4` (DomSanitizer fix + evidence config centralisation).
- Tags `v0.1.0` and `v0.1.1` pushed to origin; both have GitHub
  Releases with markdown notes visible on the repo homepage sidebar.
- Live URL healthy: landing (`/`) + dashboard (`/live`) + evidence
  (`/evidence`) + pulse animation + mobile viewport + direct URL entry
  all working. CORS locked to specific origins via Bicep-wired
  `webAppOrigin` on `compute.bicep`.
- Evidence page assets: 9 PNGs at both `web/public/evidence/` (Angular
  serves) and `docs/images/` (GitHub markdown mirror). Loom video
  `7726a3e69ec84db68a86a0290c46bf62` embedded via
  `environment.evidence.loomVideoId`.
- Tests: **64 backend passing**, `ng build` clean, `bicep build` clean.
  Frontend unit tests remain deferred to SP-21.
- CI/CD pipeline: 4 jobs (`lint-test` → `deploy-infra` → `publish-app`
  + `publish-web` in parallel). All Node 24, zero deprecation warnings.
- Debug stories at `docs/sp1-13-debug-stories.md` (gitignored):
  - **#1** — SWA `skip_app_build` reinterprets `app_location` as the
    artifact folder and ignores `output_location`. Buried in prose docs.
  - **#2 ★** — Angular `DomSanitizer` silently blanks `iframe[src]`
    bound to a plain string; textbook Angular-security material.

### Wrap tasks shipped this session (2026-07-16)

- **DomSanitizer fix** (`20518d4`) — Loom iframe was rendering blank
  because Angular's default sanitizer strips string URLs bound into
  `iframe[src]` (`SecurityContext.RESOURCE_URL`). Fixed by wrapping
  the URL with `bypassSecurityTrustResourceUrl()`.
- **Evidence config centralisation** (same commit) — release metadata
  + Loom ID + repo/live URLs moved into `environment.evidence.*` so
  dev vs prod values live in config, not component code. Both env
  files now shape-symmetric.
- **Debug story #2 ★ written** for the DomSanitizer gotcha.
- **progress.md flip** — SP1-13 row → ✅, SP1-13 prose section
  rewritten as "closed 2026-07-16", this handoff refreshed.
- **Jira SP-13 → Done** transition + completion comment (pending
  developer approval at session close, per memory
  `feedback_jira_approval`).

### Post-Sprint-1 stack (top of stack now)

Per memory `project_post_sprint1_full_time_prep` +
`feedback_minimise_context_switching` — one primary focus per day, no
context switching. Priority order:

1. **Q-Bank study plan (Week 3 of 5)** — 202-Q doc at
   `C:\BUDDHIKA\2026 July\Interview-Question-Bank.docx`. Schedule Fri
   26 Jun → Thu 31 Jul. Active recall, speak don't read. Memory
   `project_interview_prep_study_plan`.
2. **Advanced C# brush-up (2 h/day, Jul 1 → Aug 1)** — 62 h syllabus
   from `C:\BUDDHIKA\SydPulse-P6\AdvancedC#`. Target: sound-senior on
   concurrency. Sibling to Q-Bank + System Design.
3. **System Design prep** — reference doc
   `C:\BUDDHIKA\2026 July\SystemDesign.docx` (147 KB); Prep1/Prep2 Q&A
   docs; 60+ audio rehearsals. Memory `reference_july2026_prep_folder`.
4. **CV surgical pass** — swap placeholder URLs for real live URL,
   replace TfNSW-quota proxies with measured p99 numbers from App
   Insights / Cosmos / SignalR diagnostics. **Gates any CV send.**
   Memory `project_sp1_close_cv_followups`.
5. **Narrated Loom re-record + LinkedIn post** — silent-camera version
   already uploaded. Script draft in memory
   `project_post_sprint1_narrated_loom_linkedin`; timing locked at
   "after Week 1 study + first CV batch sent."

### Sprint 2 status — PAUSED

Backlog captured; do not start any Sprint 2 item until the interview
cycle settles. Existing Sprint 2 deferrals:

- **Frontend unit tests → [SP-21](https://gsoft85512.atlassian.net/browse/SP-21).**
  Decided 2026-06-23 during SP1-09. Target roles are .NET-senior with
  Angular secondary; backend already at 64 tests.
- **`VehicleWireDto` refactor → Sprint 2.** Captured in
  `StateWriterFunction.cs` code comment. Would decouple wire from
  storage cleanly if the trade-off is worth pursuing.
- **Bankstown route catalogue gap.** BNK_1a / BNK_1c chip labels fall
  back to raw TfNSW routeIds because the static catalogue lacks
  entries. Surfaced during Debug Story #10.
- **Freshness-ring liveness indicator → Sprint 2**
  (memory `project_sp2_freshness_ring_deferred.md`). Ops-inspector
  value; pairs with demo mode.
- **Demo mode (fixture-based Poller replay) → Sprint 2 headline**
  (memory `project_sp2_demo_mode_headline.md`). Unblocks off-peak
  interview demos; fully specified in `docs/modes.md`.
- **SP-19 Archiver smoke** (memory
  `project_sp19_archiver_smoke_deferred.md`) — descoped from SP1-16
  because Sprint 1 frontend is Commuter-only. Must complete before
  Sprint 3 Analytics view starts.
- **Azure cost analysis** (memory
  `project_sp2_azure_cost_analysis.md`) — verify actual RG spend
  before committing to "cycled off" messaging; audit DevPulseRG too.

### Resume sequence (next session)

1. Follow session start protocol per `CLAUDE.md` — read this file, then
   `sprint-1.md`, then glob `docs/**/*.md`. Also read
   `C:\BUDDHIKA\2026 July\CLAUDE.md` per the auto-read memory.
2. `git status` + `git log -3 --oneline` — expect on `main` at
   `20518d4` or later, clean working tree.
3. Ask developer which post-Sprint-1 block they want to run today —
   Q-Bank, Advanced C#, System Design, CV surgical pass, or narrated
   Loom. Respect `feedback_minimise_context_switching` — pick one,
   block-schedule it, don't fan out.

### Standing operating rules

- User runs all Azure mutations. Claude provides instructions and
  read-only verification only.
- **Developer handles git + PR workflow.** Claude reminds Socratically,
  no `git` or `gh` mutations from Claude.
- Feature branches + PR for all sprint items.
- One file at a time. Stop after each step and wait for explicit approval.
- Claude cannot read/write `**/local.settings.json` (deny rule).
- Windows / PowerShell — single-line commands only.
- Code comments convention active on all source files Claude writes.
- No magic strings for Azure infrastructure names — use `FunctionConstants`.
