# Functions (.NET 8 isolated worker)

Context for Claude Code when working in `/functions/`. The root `CLAUDE.md`
covers project-wide rules; this file covers .NET-specific patterns.

## Solution layout

```
SydneyPulse.Functions/    Host project, isolated-worker model
  AzFunctions/            All Azure Functions, grouped by purpose
    EventPipeline/        Poller, StateWriter, Alerter (event-driven)
    HttpApi/              Vehicles, Alerts, Routes, Negotiate (HTTP-triggered)
    Spikes/               De-risking spikes only (not production)
  Program.cs              DI configuration, hosted services
  host.json               Runtime config (sampling, timeouts)
  local.settings.json     LOCAL ONLY — never commit
SydneyPulse.Core/         Models, business logic, TfNsw client
  TfNsw/                  TfNswFeedClient, GTFS-realtime decoding
  Events/                 CloudEvent record types (VehicleUpdate, ServiceAlert)
  Cosmos/                 DocumentDB entity types
SydneyPulse.Tests/        xUnit
  Unit/                   Class-level tests with mocks
    AzFunctions/          Mirrors source layout
      EventPipeline/      Tests for Poller, StateWriter, Alerter
      HttpApi/            Tests for Vehicles, Alerts, Routes
  Integration/            Tests against Azurite + emulated Service Bus
  Fixtures/               Sample GTFS payloads, recorded snapshots
```

## Function patterns

- **One Function per file.** Don't combine multiple triggers in a class.
- **Functions are thin.** They wire triggers to services. Business logic
  lives in `SydneyPulse.Core` and is unit-testable without the Functions
  host.
- **Records for events.** Event types are `record` (immutable, value
  equality). `record VehicleUpdate(string VehicleId, ...)`.
- **DI for everything.** `IHttpClientFactory`, `TfNswFeedClient`,
  `CosmosClient`, `ILogger<T>`. Inject through Function class constructor.
- **Cancellation tokens are mandatory.** Every async method takes
  `CancellationToken` and propagates it.

Example Function skeleton:

```csharp
public class PollerFunction(
    ITfNswFeedClient feedClient,
    EventGridPublisherClient eventGrid,
    ILogger<PollerFunction> logger)
{
    [Function("Poller")]
    public async Task RunAsync(
        [TimerTrigger("*/30 * * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        // delegate to a service; keep this method <20 lines
    }
}
```

## DI registration in Program.cs

- `TfNswFeedClient` is **singleton** (caches GTFS static data per
  instance, see ADR-0009)
- `CosmosClient` is **singleton** (manages a connection pool internally)
- `EventGridPublisherClient` is **singleton**
- Per-Function services that hold no state are **transient**

## Configuration

- Local: `local.settings.json` (gitignored)
- Cloud: app settings via Key Vault references using
  `@Microsoft.KeyVault(SecretUri=...)` syntax
- Strongly-typed via `IOptions<TfNswOptions>` pattern. No raw
  `IConfiguration` reads outside the Options setup block in
  `Program.cs`.

## Error handling

- Polly resilience policies on the `TfNswFeedClient`'s HttpClient
  (retry on 429/503, circuit breaker). See ADR-0001.
- Don't swallow exceptions. Functions runtime will retry the trigger
  based on the binding (Service Bus retries up to `MaxDeliveryCount`,
  Event Grid retries up to 24h with exponential backoff).
- Log structured: `logger.LogWarning("Poll failed for mode {Mode}", mode)`,
  not string concatenation.

## Testing

- Unit tests use `xunit` + `Moq` for mocks
- Integration tests use Azurite for storage emulation and the official
  Service Bus emulator container
- Each Function class has a corresponding `Tests/Unit/XxxFunctionTests.cs`
- Coverage target: 70% line coverage on `SydneyPulse.Core`. Functions
  themselves are mostly wiring and don't need high coverage.

## Common tasks

- Add a new Function: create a class under the matching subfolder of
  `SydneyPulse.Functions/AzFunctions/` (`EventPipeline/` for trigger-driven
  pipeline functions, `HttpApi/` for HTTP endpoints), register dependencies
  in `Program.cs`, add a corresponding test file in the mirroring
  `SydneyPulse.Tests/Unit/AzFunctions/<group>/` folder.
- Add a new event type: create a record in `SydneyPulse.Core/Events/`
  with version suffix (`VehicleUpdate.v1`). Update `/docs/api.md`.
- Update a Cosmos schema: change the record in `SydneyPulse.Core/Cosmos/`,
  update partition key strategy if needed (currently `/routeShortName`).
  Migrations are not currently automated — document in PR.

## No magic strings for Azure infrastructure names

Never use bare string literals for Azure resource names inside Function
classes or their attributes. All infrastructure strings go in
`FunctionConstants.cs` as `internal const string`, with service-explicit
names so the Azure service is clear at the call site.

Naming convention: `<Scope><AzureService><Kind>`
- `CosmosDatabaseName`, `VehiclesCosmosContainer`, `AlertsCosmosContainer`
- `AlertsServiceBusTopic`, `AlertsServiceBusSubscription`, `ServiceBusConnectionKey`
- `VehiclesSignalRHub`, `AlertsSignalRHub`
- `VehiclesSignalRGroup`, `AlertsSignalRGroup`
- `VehicleUpdatedSignalREvent`, `AlertReceivedSignalREvent`

C# allows `const string` fields in attribute arguments, so this works:
```csharp
[SignalROutput(HubName = FunctionConstants.VehiclesSignalRHub)]
[ServiceBusTrigger(FunctionConstants.AlertsServiceBusTopic,
    FunctionConstants.AlertsServiceBusSubscription,
    Connection = FunctionConstants.ServiceBusConnectionKey)]
```

## Don't

- Don't use the in-process Functions model. We're isolated worker for
  .NET 8 LTS alignment.
- Don't add a static `HttpClient` field. Use `IHttpClientFactory`.
- Don't write to `Console.WriteLine`. Use `ILogger<T>`.
- Don't add the Application Insights NuGet package directly — it's
  configured via the Functions host. Custom telemetry goes through
  `TelemetryClient` injected via DI.
- Don't put secrets in `host.json` or `local.settings.json`. Use Key
  Vault references for cloud, environment variables for local.
