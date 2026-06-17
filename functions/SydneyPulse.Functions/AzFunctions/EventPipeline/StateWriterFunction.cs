// StateWriterFunction.cs
// ----------------------
// EG-triggered consumer of VehicleUpdate.v1 CloudEvents. Steps per event:
//   1. Deserialize CloudEvent.Data → VehicleUpdate (manual; explicit JSON opts).
//   2. Defensive guard — drop events with null/empty VehicleId or RouteShortName.
//   3. Stale-write guard — drop if Cosmos already holds a newer position.
//   4. Upsert VehicleDocument into Cosmos `vehicles` (PK = routeShortName).
//   5. Broadcast vehicleUpdated to SignalR `vehicles` hub.

using Azure.Messaging;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;

namespace SydneyPulse.Functions.AzFunctions.EventPipeline;

public class StateWriterFunction(
    CosmosClient cosmosClient,
    ILogger<StateWriterFunction> logger)
{
    [Function("StateWriter")]
    [SignalROutput(HubName = FunctionConstants.VehiclesSignalRHub)]
    public async Task<SignalRMessageAction?> RunAsync(
        [EventGridTrigger] CloudEvent cloudEvent,
        CancellationToken cancellationToken)
    {
        // Only handle VehicleUpdate events in this function; log and skip any others.
        if (cloudEvent.Type != FunctionConstants.VehicleUpdateEventType)
        {
            logger.LogWarning("Unexpected CloudEvent type: {Type}", cloudEvent.Type);
            return null;
        }

        // Defensive guard: log and skip if the event data is null,
        // rather than throwing an exception and failing the whole function invocation.
        if (cloudEvent.Data is null)
        {
            logger.LogWarning("CloudEvent data is null for event type: {Type}", cloudEvent.Type);
            return null;
        }

        // Parse the CloudEvent data into a strongly-typed VehicleUpdate.
        var vehicleUpdate = cloudEvent.Data.ToObjectFromJson<VehicleUpdate>();
        if (vehicleUpdate is null)
        {
            logger.LogWarning(
                "Json deserialization failed for CloudEvent: type={Type}, data={Data}",
                cloudEvent.Type, cloudEvent.Data?.ToString());
            return null;
        }

        // Defensive guard: TfNSW data quality is variable — some entities
        // arrive without a vehicle id or routeShortName populated. Cosmos
        // rejects ReadItem / Upsert with a null id (URI escape throws) or
        // an empty PartitionKey, so drop the event with a warning rather
        // than fail the whole invocation. Surfaced by SP1-16 cloud smoke.
        if (string.IsNullOrEmpty(vehicleUpdate.VehicleId) || string.IsNullOrEmpty(vehicleUpdate.RouteShortName))
        {
            logger.LogWarning(
                "Dropping VehicleUpdate with missing required fields: vehicleId='{VehicleId}', routeShortName='{RouteShortName}'",
                vehicleUpdate.VehicleId, vehicleUpdate.RouteShortName);
            return null;
        }

        var vehicleDocument = MapToVehicleDocument(vehicleUpdate);
        var container = cosmosClient.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.VehiclesCosmosContainer);

        // Stale-write guard: read the existing document and skip upsert if the
        // stored timestamp is already newer than this event (at-least-once delivery
        // from Event Grid can replay events out of order).
        try
        {
            var existing = await container.ReadItemAsync<VehicleDocument>(
                vehicleDocument.Id,
                new PartitionKey(vehicleDocument.RouteShortName),
                cancellationToken: cancellationToken);

            if (existing.Resource.Timestamp >= vehicleUpdate.VehicleTimestamp)
            {
                logger.LogDebug(
                    "Dropping stale event for vehicle {VehicleId}: stored={Stored} >= incoming={Incoming}",
                    vehicleUpdate.VehicleId, existing.Resource.Timestamp, vehicleUpdate.VehicleTimestamp);
                return null;
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            // No existing document — first write for this vehicle, proceed normally.
        }

        await container.UpsertItemAsync(
            vehicleDocument,
            new PartitionKey(vehicleDocument.RouteShortName),
            cancellationToken: cancellationToken);

        logger.LogInformation(
            "Upserted vehicle {VehicleId} on route {Route}",
            vehicleUpdate.VehicleId, vehicleUpdate.RouteShortName);

        // Send the cloud event payload (not the Cosmos doc) to SignalR Hub,
        // so the wire contract is independent of storage refactors.
        return new SignalRMessageAction(FunctionConstants.VehicleUpdatedSignalREvent)
        {
            Arguments = [vehicleUpdate]
        };
    }

    private static VehicleDocument MapToVehicleDocument(VehicleUpdate vehicleUpdate)
    {
        return new VehicleDocument
        {
            // One document per vehicle — upsert overwrites the previous position.
            Id = vehicleUpdate.VehicleId,
            RouteShortName = vehicleUpdate.RouteShortName,
            VehicleId = vehicleUpdate.VehicleId,
            Latitude = vehicleUpdate.Latitude,
            Longitude = vehicleUpdate.Longitude,
            Bearing = vehicleUpdate.Bearing,
            SpeedKmh = vehicleUpdate.SpeedKmh,
            RouteId = vehicleUpdate.RouteId,
            // Denormalized route metadata — event already carries these from the
            // Poller's GTFS static enrichment; no extra lookup needed (ADR-0011).
            RouteLongName = vehicleUpdate.RouteLongName,
            RouteColor = vehicleUpdate.RouteColor,
            Mode = vehicleUpdate.Mode,
            OccupancyStatus = vehicleUpdate.OccupancyStatus,

            Timestamp = vehicleUpdate.VehicleTimestamp,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

}
