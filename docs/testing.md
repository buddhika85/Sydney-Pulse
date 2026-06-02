# Sydney Pulse — Testing Guide

How to run, filter, and extend the test suite. Keep this doc up to date
as new test classes are added.

---

## Prerequisites

| Requirement | Check |
|-------------|-------|
| .NET SDK 8.0.127 (pinned by `functions/global.json`) | `dotnet --version` |

No other tools required. Tests use mocks only — no Azurite, no live Azure
connection, no local.settings.json needed.

---

## Running all tests

From the **repo root**:

```powershell
dotnet test functions/SydneyPulse.sln
```

Expected output:

```
Test Run Successful.
Total tests: 19
     Passed: 19
 Total time: ~10 Seconds
```

---

## Running a specific test project

```powershell
dotnet test functions/SydneyPulse.Tests/SydneyPulse.Tests.csproj
```

---

## Filtering to a single test class

`ClassName=` does not work with xUnit — use `FullyQualifiedName~` (substring match) instead:

```powershell
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~TfNswFeedClientTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~PollerFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~StateWriterFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~AlerterFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~VehiclesFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~AlertsFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~RoutesFunctionTests"
```

---

## Filtering to a single test method

Use a substring match on the fully-qualified name:

```powershell
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~RunAsync_WithVehicles"
```

---

## Verbose output (see each test name as it runs)

```powershell
dotnet test functions/SydneyPulse.sln --logger "console;verbosity=normal"
```

---

## Test inventory

### `SydneyPulse.Tests/Unit/TfNswFeedClientTests.cs`

Tests for `TfNswFeedClient` (GTFS-RT parsing, route cache, CSV normalisation).
HTTP is stubbed via an inline `HttpMessageHandler` — no real network calls.

| Test | What it covers |
|------|----------------|
| `GetVehiclePositionsAsync_WithValidFeed_ReturnsMappedPosition` | Protobuf decode → `VehicleUpdate` mapping; route short name + colour enrichment from static feed |
| `GetRoutesAsync_SecondCallSameMode_DoesNotFetchAgain` | 1-hour in-memory cache hit: second call for the same mode makes zero HTTP requests |
| `GetRoutesAsync_ParsesShortNameAndNormalisesColor` | GTFS stores colour without `#`; client must normalise to `#F99D1C` format |

### `SydneyPulse.Tests/Unit/AzFunctions/EventPipeline/PollerFunctionTests.cs`

Tests for `PollerFunction` (Event Grid batch publish, empty-feed guard, CloudEvent shape).
`ITfNswFeedClient` and `EventGridPublisherClient` are mocked — no Azure calls.

| Test | What it covers |
|------|----------------|
| `RunAsync_WithVehicles_PublishesOneCloudEventPerVehicle` | 2 vehicles → one `SendEventsAsync` call with a batch of 2 events, all typed `com.sydneypulse.VehicleUpdate.v1` |
| `RunAsync_WithEmptyFeeds_DoesNotCallSendEvents` | Empty vehicle + alert feeds → `SendEventsAsync` never called (no empty batch sent to Event Grid) |
| `RunAsync_WithAlerts_PublishesCorrectTypeAndSource` | 1 alert → event type is `com.sydneypulse.ServiceAlert.v1`, source is `/sydney-pulse/poller` |

### `SydneyPulse.Tests/Unit/AzFunctions/EventPipeline/StateWriterFunctionTests.cs`

Tests for `StateWriterFunction` (Cosmos upsert, stale-write guard, SignalR broadcast shape).
`CosmosClient` and `Container` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `RunAsync_NewVehicle_UpsertsDocumentAndReturnsBroadcast` | First write for a vehicle (no existing doc) → `UpsertItemAsync` called once, returned `SignalRMessageAction` targets `vehicleUpdated` on group `vehicles` |
| `RunAsync_StaleEvent_SkipsUpsertAndReturnsNull` | Incoming timestamp older than stored → `UpsertItemAsync` never called, `null` returned (no broadcast) |
| `RunAsync_NewerEvent_OverwritesExistingDocumentAndBroadcasts` | Incoming timestamp newer than stored → `UpsertItemAsync` called once, SignalR broadcast returned |

### `SydneyPulse.Tests/Unit/AzFunctions/EventPipeline/AlerterFunctionTests.cs`

Tests for `AlerterFunction` (CloudEvent unwrapping, Cosmos upsert, SignalR broadcast shape).
`CosmosClient` and `Container` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `RunAsync_ValidAlert_UpsertsDocumentAndBroadcastsToAlertsGroup` | Valid CloudEvent envelope → `UpsertItemAsync` called once with correct `alertId` + partition key; `SignalRMessageAction` targets `alertReceived` on group `alerts` |
| `RunAsync_CloudEventMissingData_ReturnsNullWithoutUpsert` | CloudEvent with no `data` field → `null` returned, `UpsertItemAsync` never called |
| `RunAsync_AlertWithNullDates_UpsertsDocumentWithNullDates` | `StartsAt` and `EndsAt` nullable fields map correctly to `null` in the `AlertDocument` |

### `SydneyPulse.Tests/Unit/AzFunctions/HttpApi/VehiclesFunctionTests.cs`

Tests for `VehiclesFunction` (Cosmos query routing, Cache-Control header, partition key scoping).
`CosmosClient` and `Container` are mocked. Uses `TestHttpRequestData` / `TestHttpResponseData`
test doubles defined in this file and shared by the other HTTP function tests.

| Test | What it covers |
|------|----------------|
| `RunAsync_Unfiltered_Returns200WithCacheControlHeader` | No query params → cross-partition query runs, response is 200 OK with `Cache-Control: public, max-age=5` |
| `RunAsync_ModeFilter_SendsModeQueryToContainer` | `?mode=sydneytrains` → Cosmos query includes `WHERE c.mode` clause |
| `RunAsync_RouteShortNameFilter_UsesPartitionScopedQuery` | `?routeShortName=T1` → partition-scoped query with `PartitionKey("T1")` |

### `SydneyPulse.Tests/Unit/AzFunctions/HttpApi/AlertsFunctionTests.cs`

Tests for `AlertsFunction` (Cosmos cross-partition query, empty-container handling).
`CosmosClient` and `Container` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `RunAsync_WithAlerts_Returns200AndQueriesContainer` | Container has alerts → 200 OK, iterator called once |
| `RunAsync_EmptyContainer_Returns200WithNoAlerts` | No docs in container → 200 OK with empty `alerts` array (not a 404) |

### `SydneyPulse.Tests/Unit/AzFunctions/HttpApi/RoutesFunctionTests.cs`

Tests for `RoutesFunction` (TfNswFeedClient cache delegation, per-mode iteration).
`ITfNswFeedClient` is mocked — no network calls.

| Test | What it covers |
|------|----------------|
| `RunAsync_WithRoutes_Returns200AndCallsFeedClientPerMode` | Two configured modes → `GetRoutesAsync` called twice, response 200 OK |
| `RunAsync_EmptyFeed_Returns200WithEmptyRoutesArray` | Feed returns empty dictionary → 200 OK with empty `routes` array |

---

## Coverage

Run with the built-in coverlet collector:

```powershell
dotnet test functions/SydneyPulse.sln --collect "XPlat Code Coverage"
```

Coverage reports land in `functions/SydneyPulse.Tests/TestResults/`. Target
is 70% line coverage on `SydneyPulse.Core` (see `functions/CLAUDE.md`).
Function classes themselves are thin wiring and do not need high coverage.

---

## Adding new tests

- One test file per class: `Tests/Unit/<ClassName>Tests.cs`
- Follow the Arrange / Act / Assert structure used in existing tests
- Unit tests mock all external dependencies (HTTP, Azure SDK clients)
- Integration tests (Azurite + Service Bus emulator) go in `Tests/Integration/`
  — none exist yet; see `functions/CLAUDE.md` for the planned setup
