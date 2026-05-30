// StateWriterFunctionTests.cs
// ---------------------------
// Unit tests for StateWriterFunction.
// CosmosClient and Container are mocked — no Azure connection required.

using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions.Functions;
using Xunit;

namespace SydneyPulse.Tests.Unit;

public class StateWriterFunctionTests
{
    private readonly Mock<CosmosClient> _cosmosClientMock = new();
    private readonly Mock<Container> _containerMock = new();

    public StateWriterFunctionTests()
    {
        // Wire CosmosClient.GetContainer to return our mock container.
        _cosmosClientMock
            .Setup(c => c.GetContainer("sydneyPulse", "vehicles"))
            .Returns(_containerMock.Object);
    }

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
        var update = MakeUpdate();

        // Act
        var result = await fn.RunAsync(update, CancellationToken.None);

        // Assert — upsert was called and a SignalR broadcast was returned.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.Is<VehicleDocument>(d => d.VehicleId == "VH-001" && d.RouteShortName == "T1"),
            It.IsAny<PartitionKey>(), It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.NotNull(result);
        Assert.Equal("vehicleUpdated", result!.Target);
        Assert.Equal("vehicles", result.GroupName);
    }

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
        var update = MakeUpdate(timestamp: incomingTimestamp);

        // Act
        var result = await fn.RunAsync(update, CancellationToken.None);

        // Assert — stale event: no upsert, no broadcast.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        Assert.Null(result);
    }

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
        var update = MakeUpdate(vehicleId: "VH-002", timestamp: incomingTimestamp);

        // Act
        var result = await fn.RunAsync(update, CancellationToken.None);

        // Assert — newer event: upsert runs and SignalR broadcast is returned.
        _containerMock.Verify(c => c.UpsertItemAsync(
            It.IsAny<VehicleDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.NotNull(result);
        Assert.Equal("vehicleUpdated", result!.Target);
    }
}
