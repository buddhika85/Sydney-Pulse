// AlertsFunctionTests.cs
// -----------------------
// Unit tests for AlertsFunction.
// CosmosClient and Container are mocked — no Azure connection required.
// Uses TestHttpRequestData / TestHttpResponseData from VehiclesFunctionTests.cs.

using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Functions;
using SydneyPulse.Functions.AzFunctions.HttpApi;
using Xunit;

namespace SydneyPulse.Tests.Unit.AzFunctions.HttpApi;

public class AlertsFunctionTests
{
    private readonly Mock<CosmosClient> _cosmosClientMock = new();
    private readonly Mock<Container> _containerMock = new();

    public AlertsFunctionTests()
    {
        _cosmosClientMock
            .Setup(c => c.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.AlertsCosmosContainer))
            .Returns(_containerMock.Object);
    }

    private static AlertDocument MakeAlert(string alertId = "ALERT-001") => new()
    {
        Id = alertId,
        AlertId = alertId,
        RouteShortName = "T1",
        Severity = "delay",
        HeaderText = "T1 Western Line delays",
        DescriptionText = "8 min delays due to signal fault",
        StartsAt = DateTimeOffset.UtcNow.AddMinutes(-30),
        EndsAt = null,
        ReceivedAt = DateTimeOffset.UtcNow,
    };

    // Wires the container mock to return a single-page iterator with the given docs.
    private void SetupIterator(params AlertDocument[] docs)
    {
        var mockPage = new Mock<FeedResponse<AlertDocument>>();
        mockPage.Setup(p => p.GetEnumerator()).Returns(docs.AsEnumerable().GetEnumerator());

        var mockIterator = new Mock<FeedIterator<AlertDocument>>();
        mockIterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        mockIterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(mockPage.Object);

        _containerMock
            .Setup(c => c.GetItemQueryIterator<AlertDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(mockIterator.Object);
    }

    // Container has alerts — returns 200 and calls the Cosmos iterator once.
    [Fact]
    public async Task RunAsync_WithAlerts_Returns200AndQueriesContainer()
    {
        SetupIterator(MakeAlert("ALERT-001"), MakeAlert("ALERT-002"));
        var fn = new AlertsFunction(_cosmosClientMock.Object, NullLogger<AlertsFunction>.Instance);

        var result = await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/alerts"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        _containerMock.Verify(c => c.GetItemQueryIterator<AlertDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()), Times.Once);
    }

    // Empty container — returns 200 with an empty alerts array (not a 404).
    [Fact]
    public async Task RunAsync_EmptyContainer_Returns200WithNoAlerts()
    {
        SetupIterator(); // no docs
        var fn = new AlertsFunction(_cosmosClientMock.Object, NullLogger<AlertsFunction>.Instance);

        var result = await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/alerts"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        _containerMock.Verify(c => c.GetItemQueryIterator<AlertDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()), Times.Once);
    }
}
