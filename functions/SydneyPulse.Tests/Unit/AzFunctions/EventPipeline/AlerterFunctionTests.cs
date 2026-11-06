// AlerterFunctionTests.cs
// -----------------------
// Unit tests for AlerterFunction: CloudEvent unwrapping, Cosmos upsert, SignalR broadcast.

using FluentAssertions;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Core.Events;
using SydneyPulse.Functions;
using SydneyPulse.Functions.AzFunctions.EventPipeline;
using System.Text.Json;

namespace SydneyPulse.Tests.Unit.AzFunctions.EventPipeline;

public class AlerterFunctionTests
{
    private readonly Mock<Container> _mockContainer;
    private readonly Mock<ILogger<AlerterFunction>> _mockLogger;
    private readonly AlerterFunction _function;

    public AlerterFunctionTests()
    {
        var mockClient = new Mock<CosmosClient>();
        _mockContainer = new Mock<Container>();
        _mockLogger = new Mock<ILogger<AlerterFunction>>();

        mockClient
            .Setup(c => c.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.AlertsCosmosContainer))
            .Returns(_mockContainer.Object);

        _mockContainer
            .Setup(c => c.ReadItemAsync<AlertDocument>(
                It.IsAny<string>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new CosmosException("Not Found", System.Net.HttpStatusCode.NotFound, 0, null, 0));

        // UpsertItemAsync is abstract on Container — Moq handles it
        _mockContainer
            .Setup(c => c.UpsertItemAsync(
                It.IsAny<AlertDocument>(),
                It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Mock.Of<ItemResponse<AlertDocument>>());

        _function = new AlerterFunction(mockClient.Object, _mockLogger.Object);
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

    // Staleguard Test 1: an older alert is skipped.
    // The stored alert has a newer timestamp than the incoming one, so the function does not write,
    // and logs that it was stale
    [Fact]
    public async Task RunAsync_StaleAlert_SkipsUpsertAndReturnsNull()
    {
        // Arrange — stored document is NEWER than the incoming event.
        var storedTimestamp = DateTimeOffset.UtcNow;
        var incomingTimestamp = storedTimestamp.AddSeconds(-10); // older

        var existingDoc = new AlertDocument
        {
            Id = "ALT-001",
            RouteShortName = "T1",
            AlertId = "ALT-001",
            Severity = "significant_delays",
            HeaderText = "Delays on T1",
            DescriptionText = "Significant delays due to trackwork",
            StartsAt = DateTimeOffset.UtcNow,
            EndsAt = null,
            ReceivedAt = DateTimeOffset.UtcNow,
            TfNswAlertTimestamp = storedTimestamp
        };

        var responseMock = new Mock<ItemResponse<AlertDocument>>();
        responseMock.SetupGet(r => r.Resource).Returns(existingDoc);

        _mockContainer
            .Setup(c => c.ReadItemAsync<AlertDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        var incomingStaleAlert = MakeAlert() with
        {
            AlertId = existingDoc.AlertId,
            RouteShortName = existingDoc.RouteShortName,
            Severity = existingDoc.Severity,
            HeaderText = existingDoc.HeaderText,
            DescriptionText = existingDoc.DescriptionText,
            StartsAt = existingDoc.StartsAt,
            EndsAt = existingDoc.EndsAt,
            TfNswAlertTimestamp = incomingTimestamp
        };
        var messageBody = MakeCloudEventMessage(incomingStaleAlert);

        // Act - Execute Alerter azure function
        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        // Assert — read called once, stale event: no upsert, no broadcast and log is written.
        _mockContainer.Verify(x => x.ReadItemAsync<AlertDocument>(
            It.Is<string>(id => id == incomingStaleAlert.AlertId),
            It.Is<PartitionKey>(key => key == new PartitionKey(incomingStaleAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.IsAny<AlertDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyLogged(LogLevel.Debug,
                    $"Dropping stale event for alert {incomingStaleAlert.AlertId}",
                    Times.Once());

        result.Should().BeNull();
    }

    // Staleguard Test 2: a newer alert replaces the stored one.
    // The incoming timestamp is later, so the function writes it.
    [Fact]
    public async Task RunAsync_NewerAlert_UpsertsAndBroadcasts()
    {
        // Arrange — stored document is OLDER than the incoming event.
        var storedTimestamp = DateTimeOffset.UtcNow;
        var incomingTimestamp = storedTimestamp.AddSeconds(+10); // newer

        var existingDoc = new AlertDocument
        {
            Id = "ALT-001",
            RouteShortName = "T1",
            AlertId = "ALT-001",
            Severity = "significant_delays",
            HeaderText = "Delays on T1",
            DescriptionText = "Significant delays due to trackwork",
            StartsAt = DateTimeOffset.UtcNow,
            EndsAt = null,
            ReceivedAt = DateTimeOffset.UtcNow,
            TfNswAlertTimestamp = storedTimestamp
        };

        var responseMock = new Mock<ItemResponse<AlertDocument>>();
        responseMock.SetupGet(r => r.Resource).Returns(existingDoc);

        _mockContainer
            .Setup(c => c.ReadItemAsync<AlertDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        var incomingNewAlert = MakeAlert() with
        {
            AlertId = existingDoc.AlertId,
            RouteShortName = existingDoc.RouteShortName,
            Severity = existingDoc.Severity,
            HeaderText = existingDoc.HeaderText,
            DescriptionText = existingDoc.DescriptionText,
            StartsAt = existingDoc.StartsAt,
            EndsAt = existingDoc.EndsAt,
            TfNswAlertTimestamp = incomingTimestamp
        };
        var messageBody = MakeCloudEventMessage(incomingNewAlert);

        // Act - Execute Alerter azure function
        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        // Assert — read called once, newer event, so upsert once with the new timestamp
        _mockContainer.Verify(x => x.ReadItemAsync<AlertDocument>(
            It.Is<string>(id => id == incomingNewAlert.AlertId),
            It.Is<PartitionKey>(key => key == new PartitionKey(incomingNewAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.Is<AlertDocument>(x => x.TfNswAlertTimestamp == incomingTimestamp),
            It.Is<PartitionKey>(x => x == new PartitionKey(incomingNewAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        VerifyLogged(LogLevel.Debug,
                    $"Dropping stale event for alert {incomingNewAlert.AlertId}",
                    Times.Never());

        result.Should().NotBeNull();
        result!.Target.Should().Be(FunctionConstants.AlertReceivedSignalREvent);
    }

    // Staleguard Test 3: a brand-new alert is written.
    // There is nothing stored for that alert ID, so the read finds no document.
    // In Cosmos, that shows up as a not-found error, not an empty result. The function must treat it as "go ahead and write", not as a failure.
    [Fact]
    public async Task RunAsync_NewAlert_InsertsAndBroadcasts()
    {
        // Arrange
        var incomingTimestamp = DateTimeOffset.UtcNow;
        // check constructor of this class - _mockContainer already throws cosmos not-found for ReadItemAsync,
        // so we can just call the function with a new alert.

        var incomingNewAlert = MakeAlert() with
        {
            TfNswAlertTimestamp = incomingTimestamp
        };
        var messageBody = MakeCloudEventMessage(incomingNewAlert);

        // Act - Execute Alerter azure function
        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        // Assert — read called once, new event, so insert (by calling upsert on cosmos container) once with the new timestamp
        _mockContainer.Verify(x => x.ReadItemAsync<AlertDocument>(
            It.Is<string>(id => id == incomingNewAlert.AlertId),
            It.Is<PartitionKey>(key => key == new PartitionKey(incomingNewAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.Is<AlertDocument>(x => x.TfNswAlertTimestamp == incomingTimestamp),
            It.Is<PartitionKey>(x => x == new PartitionKey(incomingNewAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        VerifyLogged(LogLevel.Debug,
                    $"Dropping stale event for alert {incomingNewAlert.AlertId}",
                    Times.Never());

        result.Should().NotBeNull();
        result!.Target.Should().Be(FunctionConstants.AlertReceivedSignalREvent);
    }

    // Staleguard Test 4: The stored alert and the incoming one carry exactly the same timestamp.
    // That usually means the same message was delivered twice, which Service Bus can do.
    // The function should not write again, and should log it as a duplicate or stale. 
    [Fact]
    public async Task RunAsync_SameTimestampedAlert_SkipsUpsertAndReturnsNull()
    {
        // Arrange — stored document has SAME time stamp as the incoming event.
        var storedTimestamp = DateTimeOffset.UtcNow;
        var incomingTimestamp = storedTimestamp; // same timestamp

        var existingDoc = new AlertDocument
        {
            Id = "ALT-001",
            RouteShortName = "T1",
            AlertId = "ALT-001",
            Severity = "significant_delays",
            HeaderText = "Delays on T1",
            DescriptionText = "Significant delays due to trackwork",
            StartsAt = DateTimeOffset.UtcNow,
            EndsAt = null,
            ReceivedAt = DateTimeOffset.UtcNow,
            TfNswAlertTimestamp = storedTimestamp
        };

        var responseMock = new Mock<ItemResponse<AlertDocument>>();
        responseMock.SetupGet(r => r.Resource).Returns(existingDoc);

        _mockContainer
            .Setup(c => c.ReadItemAsync<AlertDocument>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(),
                It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);

        var incomingDuplicateAlert = MakeAlert() with
        {
            AlertId = existingDoc.AlertId,
            RouteShortName = existingDoc.RouteShortName,
            Severity = existingDoc.Severity,
            HeaderText = existingDoc.HeaderText,
            DescriptionText = existingDoc.DescriptionText,
            StartsAt = existingDoc.StartsAt,
            EndsAt = existingDoc.EndsAt,
            TfNswAlertTimestamp = incomingTimestamp
        };
        var messageBody = MakeCloudEventMessage(incomingDuplicateAlert);

        // Act - Execute Alerter azure function
        var result = await _function.RunAsync(messageBody, CancellationToken.None);

        // Assert — read called once, stored time stamp == incoming timestamp: no upsert, no broadcast and log is written.
        _mockContainer.Verify(x => x.ReadItemAsync<AlertDocument>(
            It.Is<string>(id => id == incomingDuplicateAlert.AlertId),
            It.Is<PartitionKey>(key => key == new PartitionKey(incomingDuplicateAlert.RouteShortName)),
            It.IsAny<ItemRequestOptions>(),
            It.IsAny<CancellationToken>()),
            Times.Once);

        _mockContainer.Verify(c => c.UpsertItemAsync(
            It.IsAny<AlertDocument>(), It.IsAny<PartitionKey>(),
            It.IsAny<ItemRequestOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);

        VerifyLogged(LogLevel.Debug,
                    $"Dropping stale event for alert {incomingDuplicateAlert.AlertId}",
                    Times.Once());

        result.Should().BeNull();
    }

    // TO DO: Staleguard Test 5: a stored alert with no timestamp is overwritten. The stored document was saved before the TfNswAlertTimestamp field existed, so its value is null and there is nothing to compare against. The function cannot call the incoming alert stale, so it must write it, and broadcast.
    // RunAsync_StoredAlertWithoutTimestamp_UpsertsAndBroadcasts

    // TO DO: Staleguard Test 6: an incoming alert with no timestamp is written. The message was published before the poller started sending TfNswAlertTimestamp, so its value is null. The function cannot prove it is stale, so it writes it, and broadcasts. A warning log is optional here, depending on whether you add one to the guard.
    // RunAsync_IncomingAlertWithoutTimestamp_UpsertsAndBroadcasts

    // --- helpers ---

    private static ServiceAlert MakeAlert() => new(
        AlertId: "ALT-001",
        RouteShortName: "T1",
        Severity: "significant_delays",
        HeaderText: "Delays on T1",
        DescriptionText: "Significant delays due to trackwork",
        StartsAt: DateTimeOffset.UtcNow.AddMinutes(5),
        EndsAt: null,
        TfNswAlertTimestamp: DateTimeOffset.UtcNow);

    // Builds a single-object CloudEvent JSON envelope wrapping the alert payload.
    // camelCase matches what the Azure SDK produces when the Poller publishes events.
    // AlerterFunction uses PropertyNameCaseInsensitive = true so either casing deserialises correctly.
    private static string MakeCloudEventMessage(ServiceAlert alert)
    {
        var dataJson = JsonSerializer.Serialize(alert,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        return $$"""{"specversion":"1.0","id":"test-id","source":"/sydney-pulse/test","type":"com.sydneypulse.ServiceAlert.v1","datacontenttype":"application/json","data":{{dataJson}}}""";
    }

    private void VerifyLogged(LogLevel level, string expectedText, Times times) =>
            _mockLogger.Verify(
                l => l.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) =>
                v.ToString()!.Contains(expectedText)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
                times);
}
