// FunctionConstants.cs
// --------------------
// Shared infrastructure string constants used across Azure Function classes.
// Centralised here so a rename (e.g. hub, container, topic) is a one-line change.

// Expose internals to the test project so FunctionConstants can be referenced in assertions.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SydneyPulse.Tests")]

namespace SydneyPulse.Functions;

internal static class FunctionConstants
{
    // --- Cosmos DB ---
    internal const string CosmosDatabaseName = "sydney-pulse";
    internal const string VehiclesCosmosContainer = "vehicles";
    internal const string AlertsCosmosContainer = "alerts";

    // --- Service Bus ---
    internal const string AlertsServiceBusTopic = "sydney-pulse-alerts";
    internal const string AlertsServiceBusSubscription = "alerter-sub";
    // Connection key — resolved to ServiceBus__fullyQualifiedNamespace in app settings
    internal const string ServiceBusConnectionKey = "ServiceBus";

    // --- SignalR hubs ---
    internal const string VehiclesSignalRHub = "vehicles";
    internal const string AlertsSignalRHub = "alerts";

    // --- SignalR event names (received by Angular clients) ---
    internal const string VehicleUpdatedSignalREvent = "vehicleUpdated";
    internal const string AlertReceivedSignalREvent = "alertReceived";

    // --- Archive (ADR-0012) ---
    // Container names — defaults; ArchiveOptions may override at runtime.
    internal const string PendingDataLakeContainer = "pending";
    internal const string ArchiveDataLakeContainer = "archive";
    // Single JSONL filename for in-flight events per partition.
    internal const string PendingEventsBlobName = "events.jsonl";
    // Single Parquet filename per finalised partition. Re-flush after a crash
    // safely overwrites this blob (block-blob with overwrite=true).
    internal const string ArchiveEventsBlobName = "events.parquet";
    // Manifest filename written per closed partition.
    internal const string ArchiveManifestBlobName = "_manifest.json";
    // CloudEvent type strings the Archiver subscribes to.
    internal const string VehicleUpdateEventType = "com.sydneypulse.VehicleUpdate.v1";
    internal const string ServiceAlertEventType = "com.sydneypulse.ServiceAlert.v1";
}
