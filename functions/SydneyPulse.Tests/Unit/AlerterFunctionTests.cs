// AlerterFunctionTests.cs
// -----------------------
// Unit tests for AlerterFunction: CloudEvent unwrapping, Cosmos upsert, SignalR broadcast.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions;
using SydneyPulse.Functions.Functions;
using Xunit;

namespace SydneyPulse.Tests.Unit;

public class AlerterFunctionTests
{
    private readonly Mock<Container> _mockContainer;
    private readonly AlerterFunction _function;

    public AlerterFunctionTests()
    {
        var mockClient = new Mock<CosmosClient>();
        _mockContainer = new Mock<Container>();
        var mockLogger = new Mock<ILogger<AlerterFunction>>();

        mockClient
            .Setup(c => c.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.AlertsCosmosContainer))
            .Returns(_mockContainer.Object);

        // UpsertItemAsync is abstract on Container — Moq handles it
        _mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<AlertDocument>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<AlertDocument>>());

        _function = new AlerterFunction(mockClient.Object, mockLogger.Object);
    }

    [Fact]
    public async Task RunAsync_ValidAlert_UpsertsDocumentAndBroadcastsToAlertsGroup()
    {
        var alert = MakeAlert();
        var messageBody = MakeCloudEventMessage(alert);

        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        // Cosmos upsert must fire with matching alertId and partition key
        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.Is<AlertDocument>(d => d.AlertId == alert.AlertId && d.RouteShortName == alert.RouteShortName),
            It.Is<PartitionKey>(pk => pk == new PartitionKey(alert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(result);
        Assert.Equal(FunctionConstants.AlertReceivedSignalREvent, result.Target);
        Assert.Equal(FunctionConstants.AlertsSignalRGroup, result.GroupName);
    }

    [Fact]
    public async Task RunAsync_CloudEventMissingData_ReturnsNullWithoutUpsert()
    {
        // CloudEvent envelope with no data field — simulates a malformed delivery
        var messageBody = """{"specversion":"1.0","id":"x","source":"/test","type":"com.sydneypulse.ServiceAlert.v1"}""";

        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        Assert.Null(result);
        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.IsAny<AlertDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_AlertWithNullDates_UpsertsDocumentWithNullDates()
    {
        // StartsAt and EndsAt are nullable — verify the document reflects null correctly
        var alert = MakeAlert() with { StartsAt = null, EndsAt = null };
        var messageBody = MakeCloudEventMessage(alert);

        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.Is<AlertDocument>(d => d.StartsAt == null && d.EndsAt == null),
            It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()), Times.Once);

        Assert.NotNull(result);
    }

    // --- helpers ---

    private static ServiceAlert MakeAlert() => new(
        AlertId: "ALT-001",
        RouteShortName: "T1",
        Severity: "significant_delays",
        HeaderText: "Delays on T1",
        DescriptionText: "Significant delays due to trackwork",
        StartsAt: DateTimeOffset.UtcNow,
        EndsAt: null);

    // Builds a single-object CloudEvent JSON envelope wrapping the alert payload.
    // camelCase matches what the Azure SDK produces when the Poller publishes events.
    // AlerterFunction uses PropertyNameCaseInsensitive = true so either casing deserialises correctly.
    private static string MakeCloudEventMessage(ServiceAlert alert)
    {
        var dataJson = JsonSerializer.Serialize(alert,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return $$"""{"specversion":"1.0","id":"test-id","source":"/sydney-pulse/test","type":"com.sydneypulse.ServiceAlert.v1","datacontenttype":"application/json","data":{{dataJson}}}""";
    }
}
