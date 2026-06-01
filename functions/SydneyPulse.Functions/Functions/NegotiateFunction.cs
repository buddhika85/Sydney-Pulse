// NegotiateFunction.cs
// --------------------
// POST /api/negotiate?hub=<hubName> — returns a short-lived SignalR connection
// token so the Angular client can open a WebSocket to the requested hub.
//
// HubName in [SignalRConnectionInfoInput] must be a compile-time constant, so
// both hubs are bound as separate parameters and the correct one is chosen at
// runtime based on the ?hub= query param (defaults to "vehicles").
//
// Angular calls this endpoint twice on startup:
//   POST /api/negotiate?hub=vehicles  → connects to the vehicles hub
//   POST /api/negotiate?hub=alerts    → connects to the alerts hub

using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;
using Microsoft.Extensions.Logging;

namespace SydneyPulse.Functions.Functions;

public class NegotiateFunction(ILogger<NegotiateFunction> logger)
{
    [Function("negotiate")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = FunctionConstants.VehiclesSignalRHub)] SignalRConnectionInfo vehiclesInfo,
        [SignalRConnectionInfoInput(HubName = FunctionConstants.AlertsSignalRHub)]   SignalRConnectionInfo alertsInfo)
    {
        var queryParams = QueryHelpers.ParseQuery(req.Url.Query);
        var hub = queryParams.TryGetValue("hub", out var h) ? h.ToString() : FunctionConstants.VehiclesSignalRHub;

        // Select the pre-resolved connection info for the requested hub.
        var connectionInfo = hub == FunctionConstants.AlertsSignalRHub ? alertsInfo : vehiclesInfo;

        if (connectionInfo is null)
        {
            logger.LogError("SignalR connection info is null for hub {Hub} — check AzureSignalRConnectionString", hub);
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("SignalR connection info unavailable — check AzureSignalRConnectionString");
            return err;
        }

        logger.LogDebug("Negotiated SignalR connection for hub {Hub}", hub);

        // SignalR JS client requires lowercase property names to detect the Azure redirect.
        var json = JsonSerializer.Serialize(new { url = connectionInfo.Url, accessToken = connectionInfo.AccessToken });
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(json);
        return response;
    }
}
