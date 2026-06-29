// VehiclesFunctionTests.cs
// -------------------------
// Unit tests for VehiclesFunction.
// CosmosClient and Container are mocked — no Azure connection required.
// TestHttpRequestData / TestHttpResponseData (defined below) are shared
// test doubles reused by AlertsFunctionTests and RoutesFunctionTests.

using System.Net;
using System.Security.Claims;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SydneyPulse.Core.Cosmos;
using SydneyPulse.Functions;
using SydneyPulse.Functions.AzFunctions.HttpApi;
using Xunit;

namespace SydneyPulse.Tests.Unit.AzFunctions.HttpApi;

public class VehiclesFunctionTests
{
    private readonly Mock<CosmosClient> _cosmosClientMock = new();
    private readonly Mock<Container> _containerMock = new();

    public VehiclesFunctionTests()
    {
        _cosmosClientMock
            .Setup(c => c.GetContainer(FunctionConstants.CosmosDatabaseName, FunctionConstants.VehiclesCosmosContainer))
            .Returns(_containerMock.Object);
    }

    private static VehicleDocument MakeDoc(string vehicleId = "VH-001", string routeShortName = "T1", string mode = "sydneytrains") => new()
    {
        Id = vehicleId,
        VehicleId = vehicleId,
        RouteShortName = routeShortName,
        RouteId = "NTH_1a",
        RouteLongName = "T1 North Shore Line",
        RouteColor = "#F99D1C",
        Mode = mode,
        Latitude = -33.8688,
        Longitude = 151.2093,
        Timestamp = DateTimeOffset.UtcNow,
        UpdatedAt = DateTimeOffset.UtcNow,
    };

    // Wires the mocked container to return a single-page iterator with the given docs.
    private void SetupIterator(params VehicleDocument[] docs)
    {
        var mockPage = new Mock<FeedResponse<VehicleDocument>>();
        mockPage.Setup(p => p.GetEnumerator()).Returns(docs.AsEnumerable().GetEnumerator());

        var mockIterator = new Mock<FeedIterator<VehicleDocument>>();
        mockIterator.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        mockIterator.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(mockPage.Object);

        _containerMock
            .Setup(c => c.GetItemQueryIterator<VehicleDocument>(
                It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
            .Returns(mockIterator.Object);
    }

    // Unfiltered call — all vehicles returned, Cache-Control header present.
    [Fact]
    public async Task RunAsync_Unfiltered_Returns200WithCacheControlHeader()
    {
        SetupIterator(MakeDoc("VH-001"), MakeDoc("VH-002"));
        var fn = new VehiclesFunction(
            _cosmosClientMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<VehiclesFunction>.Instance);

        var result = await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/vehicles"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        Assert.True(result.Headers.TryGetValues("Cache-Control", out _));
        _containerMock.Verify(c => c.GetItemQueryIterator<VehicleDocument>(
            It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()), Times.Once);
    }

    // ?mode= filter — Cosmos query must include a WHERE clause on c.mode.
    [Fact]
    public async Task RunAsync_ModeFilter_SendsModeQueryToContainer()
    {
        SetupIterator(MakeDoc());
        var fn = new VehiclesFunction(
            _cosmosClientMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<VehiclesFunction>.Instance);

        await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/vehicles?mode=sydneytrains"), CancellationToken.None);

        _containerMock.Verify(c => c.GetItemQueryIterator<VehicleDocument>(
            It.Is<QueryDefinition>(q => q.QueryText.Contains("c.mode")),
            It.IsAny<string>(), It.IsAny<QueryRequestOptions>()), Times.Once);
    }

    // ?routeShortName= filter — uses a partition-scoped query (cheaper than cross-partition).
    [Fact]
    public async Task RunAsync_RouteShortNameFilter_UsesPartitionScopedQuery()
    {
        SetupIterator(MakeDoc());
        var fn = new VehiclesFunction(
            _cosmosClientMock.Object,
            new MemoryCache(new MemoryCacheOptions()),
            NullLogger<VehiclesFunction>.Instance);

        await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/vehicles?routeShortName=T1"), CancellationToken.None);

        _containerMock.Verify(c => c.GetItemQueryIterator<VehicleDocument>(
            It.Is<QueryDefinition>(q => q.QueryText.Contains("c.routeShortName")),
            It.IsAny<string>(),
            It.Is<QueryRequestOptions>(o => o.PartitionKey == new PartitionKey("T1"))), Times.Once);
    }
}

// ---------------------------------------------------------------------------
// Shared HTTP test doubles — reused by AlertsFunctionTests, RoutesFunctionTests
// ---------------------------------------------------------------------------

// Minimal HttpRequestData for isolated-worker unit tests.
internal class TestHttpRequestData : HttpRequestData
{
    private readonly Uri _url;

    public TestHttpRequestData(string url)
        : base(Mock.Of<FunctionContext>())
    {
        _url = new Uri(url);
    }

    public override HttpHeadersCollection Headers { get; } = new HttpHeadersCollection();
    public override Stream Body { get; } = Stream.Null;
    public override Uri Url => _url;
    public override IEnumerable<ClaimsIdentity> Identities => [];
    public override string Method => "GET";
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override HttpResponseData CreateResponse() => new TestHttpResponseData(FunctionContext);
}

// Minimal HttpResponseData — captures status, headers, and body for assertions.
internal class TestHttpResponseData : HttpResponseData
{
    public TestHttpResponseData(FunctionContext context) : base(context) { }

    public override HttpStatusCode StatusCode { get; set; }

    // HttpResponseData.Headers requires both getter and setter.
    private HttpHeadersCollection _headers = new HttpHeadersCollection();
    public override HttpHeadersCollection Headers { get => _headers; set => _headers = value; }

    public override Stream Body { get; set; } = new MemoryStream();

    // HttpCookies is abstract and not needed for these tests.
    public override HttpCookies Cookies => null!;
}
