// PollerFunction.cs
// -----------------
// Timer-triggered Function — fires every 30 seconds, polls TfNSW GTFS-Realtime
// feeds for each configured transport mode, and publishes events to Event Grid.
//
// Event routing (ADR-0001):
//   VehicleUpdate.v1 → state-writer (Cosmos upsert + SignalR vehicles group)
//                    → archiver     (Data Lake)
//   ServiceAlert.v1  → alerter      (Service Bus topic → Alerter Fn → SignalR alerts group)
//                    → archiver     (Data Lake)

using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.Events;
using SydneyPulse.Core.TfNsw;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

public class PollerFunction(
    ITfNswFeedClient feedClient,
    EventGridPublisherClient eventGrid,
    IOptions<TfNswOptions> tfNswOptions,
    ILogger<PollerFunction> logger)
{
    // CloudEvent type strings must match the filter values in messaging.bicep exactly.
    //   The CloudEvent `type` string is the routing key. Event Grid evaluates each
    //   incoming event against subscriber filters and delivers it to the correct
    //   downstream components based on that type.
    private const string EventSource        = "/sydney-pulse/poller";
    private const string VehicleUpdateType  = "com.sydneypulse.VehicleUpdate.v1";
    private const string ServiceAlertType   = "com.sydneypulse.ServiceAlert.v1";

    [Function("Poller")]
    public async Task RunAsync(
        [TimerTrigger("*/30 * * * * *")] TimerInfo timer,
        CancellationToken cancellationToken)
    {
        foreach (var mode in tfNswOptions.Value.VehicleModes)
        {
            await PublishVehicleUpdatesAsync(mode, cancellationToken);
            await PublishServiceAlertsAsync(mode, cancellationToken);
        }
    }

    // Fetches vehicle positions for one mode and publishes as a single batch.
    private async Task PublishVehicleUpdatesAsync(string mode, CancellationToken cancellationToken)
    {
        var vehicles = await feedClient.GetVehiclePositionsAsync(mode, cancellationToken);
        if (vehicles.Count == 0) return;

        // VehicleUpdateType is required because Event Grid uses the CloudEvent type field to route events to the correct subscribers.
        var events = vehicles
            .Select(v => new CloudEvent(EventSource, VehicleUpdateType, v))
            .ToList();

        await eventGrid.SendEventsAsync(events, cancellationToken);
        logger.LogInformation("Published {Count} VehicleUpdate events for mode {Mode}", events.Count, mode);
    }

    // Fetches service alerts for one mode and publishes as a single batch.
    private async Task PublishServiceAlertsAsync(string mode, CancellationToken cancellationToken)
    {
        var alerts = await feedClient.GetServiceAlertsAsync(mode, cancellationToken);
        if (alerts.Count == 0) return;

        // ServiceAlertType is required because Event Grid uses the CloudEvent type field to route events to the correct subscribers.
        var events = alerts
            .Select(a => new CloudEvent(EventSource, ServiceAlertType, a))
            .ToList();

        await eventGrid.SendEventsAsync(events, cancellationToken);
        logger.LogInformation("Published {Count} ServiceAlert events for mode {Mode}", events.Count, mode);
    }
}
