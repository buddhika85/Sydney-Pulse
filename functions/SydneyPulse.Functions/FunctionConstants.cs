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
    internal const string CosmosDatabaseName      = "sydneyPulse";
    internal const string VehiclesCosmosContainer = "vehicles";
    internal const string AlertsCosmosContainer   = "alerts";

    // --- Service Bus ---
    internal const string AlertsServiceBusTopic        = "sydney-pulse-alerts";
    internal const string AlertsServiceBusSubscription = "alerter-sub";
    // Connection key — resolved to ServiceBus__fullyQualifiedNamespace in app settings
    internal const string ServiceBusConnectionKey = "ServiceBus";

    // --- SignalR hubs ---
    internal const string VehiclesSignalRHub = "vehicles";
    internal const string AlertsSignalRHub   = "alerts";

    // --- SignalR groups ---
    internal const string VehiclesSignalRGroup = "vehicles";
    internal const string AlertsSignalRGroup   = "alerts";

    // --- SignalR event names (received by Angular clients) ---
    internal const string VehicleUpdatedSignalREvent = "vehicleUpdated";
    internal const string AlertReceivedSignalREvent  = "alertReceived";
}
