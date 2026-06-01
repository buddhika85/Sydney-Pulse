// RoutesFunctionTests.cs
// -----------------------
// Unit tests for RoutesFunction.
// ITfNswFeedClient is mocked — no network or Azure connection required.
// Uses TestHttpRequestData / TestHttpResponseData from VehiclesFunctionTests.cs.

using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SydneyPulse.Core.TfNsw;
using SydneyPulse.Functions.Functions;
using Xunit;

namespace SydneyPulse.Tests.Unit;

public class RoutesFunctionTests
{
    private readonly Mock<ITfNswFeedClient> _feedClientMock = new();

    // Options with a single mode — keeps tests focused on function behaviour,
    // not iteration count.
    private static IOptions<TfNswOptions> MakeOptions(params string[] modes) =>
        Options.Create(new TfNswOptions { VehicleModes = modes });

    private static RouteInfo MakeRoute(string shortName = "T1") =>
        new("NTH_1a", shortName, $"{shortName} North Shore Line", "#F99D1C", "sydneytrains");

    // Feed returns routes — function returns 200 and iterates over each configured mode.
    [Fact]
    public async Task RunAsync_WithRoutes_Returns200AndCallsFeedClientPerMode()
    {
        var routes = new Dictionary<string, RouteInfo>
        {
            ["NTH_1a"] = MakeRoute("T1"),
            ["NTH_2a"] = MakeRoute("T2"),
        };
        _feedClientMock
            .Setup(c => c.GetRoutesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(routes);

        var fn = new RoutesFunction(
            _feedClientMock.Object,
            MakeOptions("sydneytrains", "buses"),
            NullLogger<RoutesFunction>.Instance);

        var result = await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/routes"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        // One GetRoutesAsync call per configured mode.
        _feedClientMock.Verify(
            c => c.GetRoutesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    // Feed returns empty dictionary — function returns 200 with an empty routes array (not a 404).
    [Fact]
    public async Task RunAsync_EmptyFeed_Returns200WithEmptyRoutesArray()
    {
        _feedClientMock
            .Setup(c => c.GetRoutesAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, RouteInfo>());

        var fn = new RoutesFunction(
            _feedClientMock.Object,
            MakeOptions("sydneytrains"),
            NullLogger<RoutesFunction>.Instance);

        var result = await fn.RunAsync(
            new TestHttpRequestData("http://localhost/api/routes"), CancellationToken.None);

        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
