// StateWriterFunctionTests.cs
// ---------------------------
// Unit tests for StateWriterFunction.
// CosmosClient and Container are mocked — no Azure connection required.

using Azure.Messaging;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions;
using SydneyPulse.Functions.AzFunctions.EventPipeline;
using Xunit;

namespace SydneyPulse.Tests.Unit.AzFunctions.EventPipeline;

public class StateWriterFunctionTests
{
    private readonly Mock<CosmosClient> _cosmosClientMock = new();
    private readonly Mock<Container> _containerMock = new();

    public StateWriterFunctionTests()
    {
        // Wire CosmosClient.GetContainer to return our mock container.
        _cosmosClientMock
            .Setup(c => c.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.VehiclesCosmosContainer))
            .Returns(_containerMock.Object);
    }

    // This can be either insert or update
    private static VehicleUpdate MakeUpdate(string vehicleId = "VH-001",
        DateTimeOffset? timestamp = null) => new(
            VehicleId: vehicleId,
            TripId: "TRIP-1",
            RouteId: "NTH_1a",
            RouteShortName: "T1",
            RouteLongName: "T1 North Shore Line",
            RouteColor: "#F99D1C",
            Mode: "sydneytrains",
            Latitude: -33.8688,
            Longitude: 151.2093,
            Bearing: 90f,
            SpeedKmh: 60f,
            OccupancyStatus: null,
            VehicleTimestamp: timestamp ?? DateTimeOffset.UtcNow);

    private static CloudEvent MakeCloudEvent(string vehicleId = "VH-001", DateTimeOffset? timestamp = null) => new(
           source: "/sydney-pulse/poller",
           type: FunctionConstants.VehicleUpdateEventType,
           jsonSerializableData: MakeUpdate(vehicleId: vehicleId, timestamp: timestamp));

    // Insert scenario: no existing document,
    // so ReadItemAsync throws NotFound; function should upsert and broadcast.
    [Fact]
    public async Task RunAsync_NewVehicle_UpsertsDocumentAndReturnsBroadcast()
    {
        // Arrange — no existing document (first write for this vehicle).
        _containerMock
            .Setup(c => c.ReadItemAsync<VehicleDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not found", System.Net.HttpStatusCode.NotFound, 0, "", 0));

        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<VehicleDocument>>());

        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);
        var cloudEvent = MakeCloudEvent();

        // Act - Execute StateWriterFunction azure function
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — upsert was called and a SignalR broadcast was returned.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.Is<VehicleDocument>(d =>
                d.VehicleId == "VH-001" &&
                d.RouteShortName == "T1" &&
                d.RouteLongName == "T1 North Shore Line" &&
                d.RouteColor == "#F99D1C" &&
                d.Mode == "sydneytrains" &&
                d.OccupancyStatus == null),
            It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(result);
        Assert.Equal(FunctionConstants.VehicleUpdatedSignalREvent, result!.Target);
    }

    // Update scenario: existing document is newer than incoming event;
    // function should skip upsert and return null.
    [Fact]
    public async Task RunAsync_StaleEvent_SkipsUpsertAndReturnsNull()
    {
        // Arrange — stored document is NEWER than the incoming event.
        var storedTimestamp = DateTimeOffset.UtcNow;
        var incomingTimestamp = storedTimestamp.AddSeconds(-10); // older

        var existingDoc = new VehicleDocument
        {
            Id = "VH-001",
            RouteShortName = "T1",
            VehicleId = "VH-001",
            RouteId = "NTH_1a",
            RouteLongName = "T1 North Shore Line",
            RouteColor = "#F99D1C",
            Mode = "sydneytrains",
            Timestamp = storedTimestamp,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var responseMock = new Mock<ItemResponse<VehicleDocument>>();
        responseMock.SetupGet(r => r.Resource).Returns(existingDoc);

        _containerMock
            .Setup(c => c.ReadItemAsync<VehicleDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);
        var cloudEvent = MakeCloudEvent(timestamp: incomingTimestamp);

        // Act - Execute StateWriterFunction azure function
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — stale event: no upsert, no broadcast.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(result);
    }

    // Update scenario: existing document is older than incoming event;
    [Fact]
    public async Task RunAsync_NewerEvent_OverwritesExistingDocumentAndBroadcasts()
    {
        // Arrange — incoming event is NEWER than stored; upsert should proceed.
        var storedTimestamp = DateTimeOffset.UtcNow.AddSeconds(-30);
        var incomingTimestamp = DateTimeOffset.UtcNow;

        var existingDoc = new VehicleDocument
        {
            Id = "VH-002",
            RouteShortName = "T2",
            VehicleId = "VH-002",
            RouteId = "NTH_2a",
            RouteLongName = "T2 Inner West Line",
            RouteColor = "#F99D1C",
            Mode = "sydneytrains",
            Timestamp = storedTimestamp,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

        var responseMock = new Mock<ItemResponse<VehicleDocument>>();
        responseMock.SetupGet(r => r.Resource).Returns(existingDoc);

        _containerMock
            .Setup(c => c.ReadItemAsync<VehicleDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        _containerMock
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<VehicleDocument>>());

        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);
        var cloudEvent = MakeCloudEvent(vehicleId: "VH-002", timestamp: incomingTimestamp);

        // Act - Execute StateWriterFunction azure function
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — newer event: upsert runs and SignalR broadcast is returned.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(result);
        Assert.Equal(FunctionConstants.VehicleUpdatedSignalREvent, result!.Target);
    }

    // Guard path: CloudEvent arrives with a Type that doesn't match
    // VehicleUpdate.v1 (e.g., EG sub misroute). Function should early-return
    // without touching Cosmos.
    [Fact]
    public async Task RunAsync_UnexpectedEventType_SkipsAndReturnsNull()
    {
        // Arrange — CloudEvent with a wrong type.
        var cloudEvent = new CloudEvent(
            source: "/sydney-pulse/poller",
            type:   "com.sydneypulse.UnknownEvent.v1",
            jsonSerializableData: MakeUpdate());
        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);

        // Act
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — early return, no Cosmos reads or upserts.
        Assert.Null(result);
        _containerMock.Verify(c => c.ReadItemAsync<VehicleDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Guard path: correct event type but CloudEvent.Data is null
    // (e.g., publisher bug). Defensive null check fires before deserialization.
    [Fact]
    public async Task RunAsync_NullCloudEventData_SkipsAndReturnsNull()
    {
        // Arrange — null jsonSerializableData → CloudEvent.Data is null.
        var cloudEvent = new CloudEvent(
            source: "/sydney-pulse/poller",
            type:   FunctionConstants.VehicleUpdateEventType,
            jsonSerializableData: null);
        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);

        // Act
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — early return, no Cosmos reads or upserts.
        Assert.Null(result);
        _containerMock.Verify(c => c.ReadItemAsync<VehicleDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Guard path: VehicleUpdate deserializes successfully but VehicleId is empty.
    // Defensive guard exists because Cosmos rejects ReadItem/Upsert with a
    // null/empty id; TfNSW trains feed occasionally omits vehicle.id (SP1-16).
    [Fact]
    public async Task RunAsync_EmptyVehicleId_SkipsAndReturnsNull()
    {
        // Arrange — inline construction since MakeUpdate hardcodes a valid VehicleId.
        var update = new VehicleUpdate(
            VehicleId:        "",                                  // edge case
            TripId:           "TRIP-1",
            RouteId:          "NTH_1a",
            RouteShortName:   "T1",
            RouteLongName:    "T1 North Shore Line",
            RouteColor:       "#F99D1C",
            Mode:             "sydneytrains",
            Latitude:         -33.8688,
            Longitude:        151.2093,
            Bearing:          90f,
            SpeedKmh:         60f,
            OccupancyStatus:  null,
            VehicleTimestamp: DateTimeOffset.UtcNow);

        var cloudEvent = new CloudEvent(
            source: "/sydney-pulse/poller",
            type:   FunctionConstants.VehicleUpdateEventType,
            jsonSerializableData: update);
        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);

        // Act
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — defensive guard fires before any Cosmos call.
        Assert.Null(result);
        _containerMock.Verify(c => c.ReadItemAsync<VehicleDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // Guard path: VehicleUpdate has empty RouteShortName — happens for
    // "Out of Service" trains (RouteId RTTA_DEF) where the GTFS static
    // cache has no route to look up (SP1-16 spike sample[1]).
    [Fact]
    public async Task RunAsync_EmptyRouteShortName_SkipsAndReturnsNull()
    {
        // Arrange — inline construction, mimics out-of-service rolling stock.
        var update = new VehicleUpdate(
            VehicleId:        "VH-001",
            TripId:           "TRIP-1",
            RouteId:          "RTTA_DEF",
            RouteShortName:   "",                                  // edge case
            RouteLongName:    "Out Of Service",
            RouteColor:       "#888888",
            Mode:             "sydneytrains",
            Latitude:         -33.8688,
            Longitude:        151.2093,
            Bearing:          90f,
            SpeedKmh:         60f,
            OccupancyStatus:  null,
            VehicleTimestamp: DateTimeOffset.UtcNow);

        var cloudEvent = new CloudEvent(
            source: "/sydney-pulse/poller",
            type:   FunctionConstants.VehicleUpdateEventType,
            jsonSerializableData: update);
        var fn = new StateWriterFunction(_cosmosClientMock.Object, NullLogger<StateWriterFunction>.Instance);

        // Act
        var result = await fn.RunAsync(cloudEvent, CancellationToken.None);

        // Assert — defensive guard fires before any Cosmos call.
        Assert.Null(result);
        _containerMock.Verify(c => c.ReadItemAsync<VehicleDocument>(
            It.IsAny<string>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
