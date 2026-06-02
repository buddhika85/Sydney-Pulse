// PollerFunctionTests.cs
// ----------------------
// Unit tests for PollerFunction.
// EventGridPublisherClient and ITfNswFeedClient are mocked — no real network or Azure calls.

using Azure;
using Azure.Messaging;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SydneyPulse.Core.Events;
using SydneyPulse.Core.TfNsw;
using SydneyPulse.Functions.AzFunctions.EventPipeline;

namespace SydneyPulse.Tests.Unit.AzFunctions.EventPipeline;

public class PollerFunctionTests
{
    // Mirror the constants from PollerFunction — tests break if the type strings drift.
    private const string VehicleUpdateType = "com.sydneypulse.VehicleUpdate.v1";
    private const string ServiceAlertType  = "com.sydneypulse.ServiceAlert.v1";
    private const string EventSource       = "/sydney-pulse/poller";

    private static PollerFunction CreateFunction(
        Mock<ITfNswFeedClient> feedMock,
        Mock<EventGridPublisherClient> egMock,
        string[] modes)
    {
        var options = Options.Create(new TfNswOptions { VehicleModes = modes });
        return new PollerFunction(
            feedMock.Object,
            egMock.Object,
            options,
            NullLogger<PollerFunction>.Instance);
    }

    private static VehicleUpdate MakeVehicle(string id) => new(
        id, "trip1", "NTH_1a", "T1", "North Shore Line", "#F99D1C",
        "sydneytrains", -33.8, 151.2, null, null, null, DateTimeOffset.UtcNow);

    private static ServiceAlert MakeAlert(string id) => new(
        id, "T1", "significant_delays", "Delays on T1", "Major delays", null, null);

    [Fact]
    public async Task RunAsync_WithVehicles_PublishesOneCloudEventPerVehicle()
    {
        // Arrange
        var feedMock = new Mock<ITfNswFeedClient>();
        var egMock   = new Mock<EventGridPublisherClient>();

        feedMock.Setup(f => f.GetVehiclePositionsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<VehicleUpdate> { MakeVehicle("v001"), MakeVehicle("v002") });
        feedMock.Setup(f => f.GetServiceAlertsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ServiceAlert>());

        egMock.Setup(e => e.SendEventsAsync(It.IsAny<IEnumerable<CloudEvent>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Mock.Of<Response>());

        var fn = CreateFunction(feedMock, egMock, ["sydneytrains"]);

        // Act
        await fn.RunAsync(new TimerInfo(), CancellationToken.None);

        // Assert: one batch of 2 events, all typed as VehicleUpdate
        egMock.Verify(e => e.SendEventsAsync(
            It.Is<IEnumerable<CloudEvent>>(evts =>
                evts.Count() == 2 &&
                evts.All(ev => ev.Type == VehicleUpdateType)),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RunAsync_WithEmptyFeeds_DoesNotCallSendEvents()
    {
        // Arrange: both feeds return empty — nothing to publish
        var feedMock = new Mock<ITfNswFeedClient>();
        var egMock   = new Mock<EventGridPublisherClient>();

        feedMock.Setup(f => f.GetVehiclePositionsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<VehicleUpdate>());
        feedMock.Setup(f => f.GetServiceAlertsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ServiceAlert>());

        var fn = CreateFunction(feedMock, egMock, ["sydneytrains"]);

        // Act
        await fn.RunAsync(new TimerInfo(), CancellationToken.None);

        // Assert: no publish calls — avoids sending empty batches to Event Grid
        egMock.Verify(e => e.SendEventsAsync(
            It.IsAny<IEnumerable<CloudEvent>>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_WithAlerts_PublishesCorrectTypeAndSource()
    {
        // Arrange
        var feedMock = new Mock<ITfNswFeedClient>();
        var egMock   = new Mock<EventGridPublisherClient>();

        feedMock.Setup(f => f.GetVehiclePositionsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<VehicleUpdate>());
        feedMock.Setup(f => f.GetServiceAlertsAsync("sydneytrains", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<ServiceAlert> { MakeAlert("alert-1") });

        egMock.Setup(e => e.SendEventsAsync(It.IsAny<IEnumerable<CloudEvent>>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(Mock.Of<Response>());

        var fn = CreateFunction(feedMock, egMock, ["sydneytrains"]);

        // Act
        await fn.RunAsync(new TimerInfo(), CancellationToken.None);

        // Assert: type matches messaging.bicep filter; source matches the poller origin
        egMock.Verify(e => e.SendEventsAsync(
            It.Is<IEnumerable<CloudEvent>>(evts =>
                evts.Count() == 1 &&
                evts.Single().Type   == ServiceAlertType &&
                evts.Single().Source == EventSource),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}
