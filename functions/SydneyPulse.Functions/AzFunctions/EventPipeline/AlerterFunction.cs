// AlerterFunction.cs
// ------------------
// Event Grid delivers the full CloudEvent envelope to Service Bus — this function unwraps it.
// Consumes ServiceAlert.v1 CloudEvents from Service Bus topic (sydney-pulse-alerts),
// upserts each alert into Cosmos DB, and broadcasts to the SignalR alerts group.

using System.Text.Json;
using Azure.Messaging;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

public class AlerterFunction(
    CosmosClient cosmosClient,
    ILogger<AlerterFunction> logger)
{
    [Function("Alerter")]
    [SignalROutput(HubName = FunctionConstants.AlertsSignalRHub)]
    public async Task<SignalRMessageAction?> RunAsync(
        // Connection key resolves to ServiceBus__fullyQualifiedNamespace in app settings (Managed Identity)
        [ServiceBusTrigger(FunctionConstants.AlertsServiceBusTopic, FunctionConstants.AlertsServiceBusSubscription, Connection = FunctionConstants.ServiceBusConnectionKey)] string messageBody,
        CancellationToken cancellationToken)
    {
        // Event Grid delivers a CloudEvent envelope to Service Bus — unwrap to get the ServiceAlert payload
        var cloudEvents = CloudEvent.ParseMany(BinaryData.FromString(messageBody));
        var alertData = cloudEvents?.FirstOrDefault()?.Data;
        if (alertData is null)
        {
            logger.LogWarning("Received malformed or empty Service Bus message, skipping");
            return null;
        }

        // Case-insensitive: Azure SDK serialises with camelCase; our record properties are PascalCase
        var alert = JsonSerializer.Deserialize<ServiceAlert>(
            alertData.ToString(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        if (alert is null)
        {
            logger.LogWarning("Failed to deserialise ServiceAlert from CloudEvent data, skipping");
            return null;
        }

        var container = cosmosClient.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.AlertsCosmosContainer);
        // Stale-write guard: read the existing document and skip upsert if the
        // stored timestamp is already newer than this event.
        try
        {
            var existing = await container.ReadItemAsync<AlertDocument>(
                alert.AlertId,
                new PartitionKey(alert.RouteShortName),
                cancellationToken: cancellationToken);

            if (existing.Resource.TfNswAlertTimestamp >= alert.TfNswAlertTimestamp)
            {
                logger.LogDebug(
                    "Dropping stale event for alert {AlertId}: stored={Stored} >= incoming={Incoming}",
                    alert.AlertId, existing.Resource.TfNswAlertTimestamp, alert.TfNswAlertTimestamp);
                return null;
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No existing document — first write for this alert, proceed normally.
        }

        var doc = new AlertDocument
        {
            // One document per alert — upsert overwrites on repeat Event Grid delivery
            Id = alert.AlertId,
            RouteShortName = alert.RouteShortName,
            AlertId = alert.AlertId,
            Severity = alert.Severity,
            HeaderText = alert.HeaderText,
            DescriptionText = alert.DescriptionText,
            StartsAt = alert.StartsAt,
            EndsAt = alert.EndsAt,
            ReceivedAt = DateTimeOffset.UtcNow,
            TfNswAlertTimestamp = alert.TfNswAlertTimestamp
        };
        await container.UpsertItemAsync(
            doc,
            new PartitionKey(doc.RouteShortName),
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Upserted alert {AlertId} on route {Route}",
            doc.AlertId, doc.RouteShortName);

        // Broadcast to all SignalR clients subscribed to the alerts hub
        return new SignalRMessageAction(FunctionConstants.AlertReceivedSignalREvent)
        {
            Arguments = [doc]
        };
    }
}
