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
Total tests: 55
     Passed: 55
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
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~HivePartitionPathTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~ParquetArchiveWriterTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~ArchiverIngestFunctionTests"
dotnet test functions/SydneyPulse.sln --filter "FullyQualifiedName~ArchiverFlushFunctionTests"
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

### `SydneyPulse.Tests/Unit/Archive/HivePartitionPathTests.cs`

Tests for `HivePartitionPath` (Hive-style path composition + parsing, ADR-0012).
Pure-function tests — no DI, no mocks.

| Test | What it covers |
|------|----------------|
| `ForHour_UtcInput_ReturnsHivePartitionPath` | Basic format contract: `yyyy=YYYY/MM=MM/dd=DD/HH=HH` from a UTC `DateTimeOffset` |
| `ForHour_NonUtcOffset_NormalisesToUtcBeforeFormatting` | Sydney time `+10:00` is normalised to UTC before formatting (HH=04, not HH=14) |
| `ForHour_SingleDigitValues_ArePaddedWithLeadingZeros` | `MM=01`, `dd=05`, `HH=03` zero-padding — required for partition pruning |
| `ForHour_EndOfHourInput_ReturnsContainingHour` | `14:59:59` belongs to `HH=14`, not `HH=15` |
| `ForFile_UtcInputWithParquetFilename_ComposesFullPath` | Hour path + `/` + filename composition |
| `ForFile_NonUtcOffset_NormalisesToUtcInPath` | UTC normalisation flows through `ForFile` (delegates to `ForHour`) |
| `ForFile_ManifestFilename_AppendsVerbatim` | Special-char filenames (`_manifest.json`) pass through unchanged |
| `Parse_WellFormedHivePath_ReturnsStartOfHourInUtc` | Inverse of `ForHour`: path → start-of-hour `DateTimeOffset` in UTC |
| `Parse_ZeroPaddedSingleDigits_ParsesCorrectly` | Single-digit-encoded values (e.g. `MM=01`) parse correctly |
| `Parse_RoundTripFromForHour_RecoversStartOfHour` | `ForHour → Parse` round-trip recovers UTC start of hour |
| `Parse_MalformedInput_Throws` | Wrong segment count throws — fail fast rather than silently mis-process |

### `SydneyPulse.Tests/Unit/Archive/ParquetArchiveWriterTests.cs`

Tests for `ParquetArchiveWriter` (Parquet schema + column pivot + writer roundtrip, ADR-0012).
Pure-function tests for `BuildSchema` / `BuildColumns` via `InternalsVisibleTo`.

| Test | What it covers |
|------|----------------|
| `BuildSchema_Returns24Columns` | Unified schema declares all 24 fields of `ArchiveEvent` |
| `BuildSchema_TimestampColumnsAreDateTimeNotDateTimeOffset` | Parquet.NET 4.x dropped `DateTimeOffset` support; schema must use `DateTime` UTC |
| `BuildColumns_EmptyEvents_ReturnsEmptyColumnsInSchemaOrder` | Order matches schema; each column array has length 0 |
| `BuildColumns_SingleVehicleUpdate_PopulatesVehicleFieldsLeavesAlertFieldsNull` | VU-shaped event populates vehicle columns; alert columns null |
| `BuildColumns_DateTimeOffsetConvertedToUtcDateTime` | Sydney-time `DateTimeOffset` lands as UTC `DateTime` in the column array |
| `WriteAsync_OneEvent_ProducesParquetReadableBack` | Roundtrip: write 1 event → `ParquetReader` reads it back → 1 row group, 24 columns |
| `WriteAsync_MultipleEvents_AllRowsRecoveredInOrder` | 3 events in → 3 rows out in the same order |

### `SydneyPulse.Tests/Unit/AzFunctions/EventPipeline/ArchiverIngestFunctionTests.cs`

Tests for `ArchiverIngestFunction` (CloudEvent → `ArchiveEvent` projection, pending-blob append, EG-trigger orchestration).
`IPendingBlobStore` and `AppendBlobClient` are mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `MapToArchiveEvent_VehicleUpdate_PopulatesVehicleFieldsAndLeavesAlertFieldsNull` | VU CloudEvent → all vehicle fields populated, all alert fields null, 3 timestamps derived correctly |
| `MapToArchiveEvent_ServiceAlert_PopulatesAlertFieldsAndLeavesVehicleFieldsNull` | SA CloudEvent → all alert fields populated, all vehicle fields null, `SourceTimestamp = StartsAt` |
| `MapToArchiveEvent_AlertWithNullStartsAt_FallsBackToCloudEventTime` | Middle fallback in `StartsAt ?? cloudEvent.Time ?? archivedAt` chain |
| `MapToArchiveEvent_AlertWithNullStartsAtAndNullTime_FallsBackToArchivedAt` | Deepest fallback (regression guard — pins that `archivedAt` is used, not `UtcNow`) |
| `MapToArchiveEvent_UnknownEventType_Throws` | Unsupported `cloudEvent.Type` throws so EG retries / dead-letters loudly |
| `AppendToPendingAsync_VehicleUpdate_AppendsJsonlToHivePartitionPath` | Blob path = Hive partition (from `SourceTimestamp`) + `events.jsonl`; JSONL ends with `\n` |
| `AppendToPendingAsync_LateEvent_PartitionPathFollowsSourceTimestampNotArchivedAt` | Late event lands in its source hour partition, not the wall-clock partition |
| `RunAsync_VehicleUpdate_AppendsAtPartitionDerivedFromVehicleTimestamp` | End-to-end orchestration: trigger → map → append; correct partition path resolved |

### `SydneyPulse.Tests/Unit/AzFunctions/EventPipeline/ArchiverFlushFunctionTests.cs`

Tests for `ArchiverFlushFunction` (closeable-partition filter, pending JSONL read, dedup, Parquet write, manifest commit, pending delete, timer orchestration).
`IPendingBlobStore`, `BlobServiceClient`, `BlobContainerClient`, `BlobClient`, `AppendBlobClient`, and `IParquetArchiveWriter` are all mocked — no Azure connection required.

| Test | What it covers |
|------|----------------|
| `ListCloseablePartitions_ThreeHourPartitions_ReturnsOnlyThoseWhoseHourEndedPastGrace` | Filter contract: hours past `(now - grace)` are closeable; current hour is not |
| `ListCloseablePartitions_PartitionExactlyAtGraceBoundary_IsCloseable` | Boundary semantics: `<=` is inclusive — partition exactly at grace edge is closeable |
| `ListCloseablePartitions_PartitionStillInProgress_IsNotCloseable` | Live partition (now inside the hour) is never finalised — Ingest may still be writing |
| `ReadPendingEvents_ThreeEvents_DeserialisesAndReturnsAllAtCorrectPath` | JSONL round-trips through deserialisation; resolver asked for the right blob path |
| `ReadPendingEvents_TrailingNewline_DoesNotProduceEmptyEntry` | `\n`-terminated content (Ingest's shape) doesn't produce a phantom empty event |
| `ReadPendingEvents_DuplicateEventIds_PassedThroughUnchanged` | No dedup at read; same `EventId` twice comes back as two events (dedup is FlushPartitionAsync's job) |
| `DedupeByEventId_DuplicatesAndUniques_KeepsOneCopyPerEventId` | Pure-function dedup contract: `GroupBy(EventId).First()` semantics, first-occurrence order preserved |
| `WriteManifest_HappyPath_WritesJsonToArchivePathWithOverwrite` | Manifest serialised to JSON, uploaded to `archive/{partitionPath}/_manifest.json` with `overwrite: true` |
| `FlushPartitionAsync_HappyPath_DedupesWritesParquetWritesManifestDeletesPending` | End-to-end orchestration: read → dedupe → Parquet upload → manifest commit → pending delete |
| `RunAsync_TwoPartitions_FlushesOnlyTheClosableOne` | Timer orchestration: list all partitions, flush only the closeable subset, leave in-progress untouched |

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
