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
| SP1-09 | Angular scaffolding (deeper)      | ⬜     | —           | —           | —                   |
| SP1-14 | Code review + interview-prep quiz (always-on) | 🔄     | 2026-06-01  | —           | —                   |
| SP1-12 | GitHub Actions CI/CD              | ⬜     | —           | —           | —                   |
| SP1-10 | Live dashboard                    | ⬜     | —           | —           | —                   |
| SP1-11 | Landing page                      | ⬜     | —           | —           | —                   |
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

## SP1-09 through SP1-13

Not started. Refer to `sprint-1.md` for scope and per-item description.

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

## Next session handoff (2026-06-20 — SP1-16 shipped)

**SP1-16 is done.** PR #8 squash-merged to `main` as `cbe32eb` on
2026-06-20. Local main in sync with origin. See **SP1-16 — Backend
visibility (manual deploy + smoke) ✅** section above for full state.

### Quick state snapshot

- On `main` at `cbe32eb`. Working tree has only a pre-existing untouched
  `M host.json` (formatting tweak, not in any SP1-16 scope).
- Tests: **55 passing**, build clean, bicep build clean.
- **Next sprint item: SP1-09 — Angular scaffolding (deeper).** Revised
  Sprint 1 order: SP1-16 → SP1-09 → SP1-12 → SP1-10 → SP1-11 → SP1-13.
  SP1-09 / SP1-10 can now target the working deployed dev URL instead of
  mocks; the captured HTTP API JSON fixtures under
  `functions/SydneyPulse.Tests/Fixtures/*-2026-06-17.json` are the
  contract reference.
- Quiz capture from SP1-16: Debug Story #20 mirrored to
  `interview-prep.md` Q2 + Word doc.

### Where we are (cumulative since SP1-08)

- Full event pipeline running end-to-end in dev: Poller → Event Grid →
  StateWriter / Alerter / (Archiver wired but smoke deferred to SP-19)
  → Cosmos + SignalR + HTTP API. `spike-deployed.html` confirms SignalR
  end-to-end.
- Bicep deployed to `sydney-pulse-rg-dev` — all resources healthy and
  smoke-verified.
- App Insights daily cap: 1 GB/day. Sampling: fixed 5% in `host.json`.
- Key Vault secrets seeded.
- Event Grid `state-writer` + `archiver` webhook subscriptions wired
  to real Function App hostname (no longer placeholders).
- Data Lake `archive` + `pending` containers provisioned + lifecycle
  policy (Hot → Cool 30d → Cold 90d).
- CORS on dev Function App tightened from `*` to `http://localhost:5500`.
- `main` branch protection enabled: PR required, force push blocked.
  CI status checks to be added at SP1-12.
- `Cosmos__AccountEndpoint` and `ServiceBus__fullyQualifiedNamespace`
  need to be in `local.settings.json` to run Functions locally.

### Resume sequence (next session)

1. Follow session start protocol per `CLAUDE.md` — read this file,
   then `sprint-1.md`, then glob `docs/**/*.md`.
2. `git status` (only `M host.json` expected) + `git log -5 --oneline`
   to confirm `cbe32eb` is HEAD.
3. **Start SP1-09 — Angular scaffolding (deeper)** per `sprint-1.md`
   row 9. TDD Phase 1 (Plan): read sprint spec, agree on files/methods.
4. Optional pre-kickoff: short quiz recall on debug stories #15 / #20
   while still fresh.

### Sprint 1 deferrals (logged to backlog)

- **Frontend unit tests → [SP-21](https://gsoft85512.atlassian.net/browse/SP-21).**
  Decided 2026-06-23 during SP1-09. Drafted a first failing
  `RealtimeService` spec to force the `Promise.all` vs `Promise.allSettled`
  design decision in `disconnect()`, then deferred the whole frontend test
  layer to keep Sprint 1 focused on shipping the live URL. Spec file
  removed; `app.component.spec.ts` (Angular CLI auto-scaffold) retained so
  `ng test` infrastructure is intact for SP-21 to build on. Rationale:
  target roles are .NET-senior with Angular secondary; backend already at
  55 tests; the `disconnect()` design question is captured in SP-21's
  description and revisited as the story's first test.

### Standing operating rules

- User runs all Azure mutations. Claude provides instructions and
  read-only verification only.
- One file at a time. Stop after each step and wait for explicit approval.
- Review pass before pushing: walk through each file, commit developer edits.
- Claude cannot read/write `**/local.settings.json` (deny rule).
- Windows / PowerShell — single-line commands only.
- Code comments convention active on all source files Claude writes.
- Feature branches + PR for all sprint items from SP1-05 onwards.
- No magic strings for Azure infrastructure names — use `FunctionConstants`.
