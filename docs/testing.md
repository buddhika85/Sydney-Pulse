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
Total tests: 12
     Passed: 12
 Total time: ~10 Seconds
```

---

## Running a specific test project

```powershell
dotnet test functions/SydneyPulse.Tests/SydneyPulse.Tests.csproj
```

---

## Filtering to a single test class

```powershell
dotnet test functions/SydneyPulse.sln --filter "ClassName=TfNswFeedClientTests"
dotnet test functions/SydneyPulse.sln --filter "ClassName=PollerFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "ClassName=StateWriterFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "ClassName=AlerterFunctionTests"
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

### `SydneyPulse.Tests/Unit/PollerFunctionTests.cs`

Tests for `PollerFunction` (Event Grid batch publish, empty-feed guard, CloudEvent shape).
`ITfNswFeedClient` and `EventGridPublisherClient` are mocked — no Azure calls.

| Test | What it covers |
|------|----------------|
| `RunAsync_WithVehicles_PublishesOneCloudEventPerVehicle` | 2 vehicles → one `SendEventsAsync` call with a batch of 2 events, all typed `com.sydneypulse.VehicleUpdate.v1` |
| `RunAsync_WithEmptyFeeds_DoesNotCallSendEvents` | Empty vehicle + alert feeds → `SendEventsAsync` never called (no empty batch sent to Event Grid) |
| `RunAsync_WithAlerts_PublishesCorrectTypeAndSource` | 1 alert → event type is `com.sydneypulse.ServiceAlert.v1`, source is `/sydney-pulse/poller` |

### `SydneyPulse.Tests/Unit/StateWriterFunctionTests.cs`

Tests for `StateWriterFunction` (Cosmos upsert, stale-write guard, SignalR broadcast shape).
`CosmosClient` and `Container` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `RunAsync_NewVehicle_UpsertsDocumentAndReturnsBroadcast` | First write for a vehicle (no existing doc) → `UpsertItemAsync` called once, returned `SignalRMessageAction` targets `vehicleUpdated` on group `vehicles` |
| `RunAsync_StaleEvent_SkipsUpsertAndReturnsNull` | Incoming timestamp older than stored → `UpsertItemAsync` never called, `null` returned (no broadcast) |
| `RunAsync_NewerEvent_OverwritesExistingDocumentAndBroadcasts` | Incoming timestamp newer than stored → `UpsertItemAsync` called once, SignalR broadcast returned |

### `SydneyPulse.Tests/Unit/AlerterFunctionTests.cs`

Tests for `AlerterFunction` (CloudEvent unwrapping, Cosmos upsert, SignalR broadcast shape).
`CosmosClient` and `Container` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `RunAsync_ValidAlert_UpsertsDocumentAndBroadcastsToAlertsGroup` | Valid CloudEvent envelope → `UpsertItemAsync` called once with correct `alertId` + partition key; `SignalRMessageAction` targets `alertReceived` on group `alerts` |
| `RunAsync_CloudEventMissingData_ReturnsNullWithoutUpsert` | CloudEvent with no `data` field → `null` returned, `UpsertItemAsync` never called |
| `RunAsync_AlertWithNullDates_UpsertsDocumentWithNullDates` | `StartsAt` and `EndsAt` nullable fields map correctly to `null` in the `AlertDocument` |

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
