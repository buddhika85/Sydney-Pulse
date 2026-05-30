// StateWriterFunction.cs
// ----------------------
// Consumes VehicleUpdate.v1 CloudEvents from Event Grid, upserts the latest
// vehicle position into Cosmos DB, and broadcasts to the SignalR vehicles group.
// Stale-write guard: incoming events older than the stored timestamp are dropped.

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;

namespace SydneyPulse.Functions.Functions;

public class StateWriterFunction(
    CosmosClient cosmosClient,
    ILogger<StateWriterFunction> logger)
{
    [Function("StateWriter")]
    [SignalROutput(HubName = FunctionConstants.VehiclesSignalRHub)]
    public async Task<SignalRMessageAction?> RunAsync(
        [EventGridTrigger] VehicleUpdate update,
        CancellationToken cancellationToken)
    {
        var container = cosmosClient.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.VehiclesCosmosContainer);

        var doc = new VehicleDocument
        {
            // One document per vehicle — upsert overwrites the previous position.
            Id = update.VehicleId,
            RouteShortName = update.RouteShortName,
            VehicleId = update.VehicleId,
            Latitude = update.Latitude,
            Longitude = update.Longitude,
            Bearing = update.Bearing,
            SpeedKmh = update.SpeedKmh,
            RouteId = update.RouteId,
            // Denormalized route metadata — event already carries these from the
            // Poller's GTFS static enrichment; no extra lookup needed (ADR-0011).
            RouteLongName = update.RouteLongName,
            RouteColor = update.RouteColor,
            Mode = update.Mode,
            OccupancyStatus = update.OccupancyStatus,

            Timestamp = update.VehicleTimestamp,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        // Stale-write guard: read the existing document and skip upsert if the
        // stored timestamp is already newer than this event (at-least-once delivery
        // from Event Grid can replay events out of order).
        try
        {
            var existing = await container.ReadItemAsync<VehicleDocument>(
                doc.Id,
                new PartitionKey(doc.RouteShortName),
                cancellationToken: cancellationToken);

            if (existing.Resource.Timestamp >= update.VehicleTimestamp)
            {
                logger.LogDebug(
                    "Dropping stale event for vehicle {VehicleId}: stored={Stored} >= incoming={Incoming}",
                    update.VehicleId, existing.Resource.Timestamp, update.VehicleTimestamp);
                return null;
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No existing document — first write for this vehicle, proceed normally.
        }

        await container.UpsertItemAsync(
            doc,
            new PartitionKey(doc.RouteShortName),
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Upserted vehicle {VehicleId} on route {Route}",
            update.VehicleId, update.RouteShortName);

        // Broadcast the updated position to all SignalR clients subscribed to the vehicles hub.
        return new SignalRMessageAction(FunctionConstants.VehicleUpdatedSignalREvent)
        {
            Arguments = [update],
            GroupName = FunctionConstants.VehiclesSignalRGroup,
        };
    }
}
