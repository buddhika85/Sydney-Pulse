// NegotiateFunction
// -----------------
// Returns the SignalR connection info (URL + access token) so the
// Angular client can open a WebSocket to the "spike" hub.
//
// This is the standard SignalR Serverless handshake:
// 1) Angular Client calls /api/negotiate
// 2) Azure generates a short‑lived access token
// 3) Client uses it to connect to the hub
//
// Note: Anonymous is OK for the spike. Production will require auth.

using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;

namespace SydneyPulse.Functions.Functions;

public class NegotiateFunction
{
    [Function("negotiate")]
    public async Task<HttpResponseData> Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req,
        [SignalRConnectionInfoInput(HubName = "spike")] SignalRConnectionInfo connectionInfo)
    {
        // Isolated worker requires explicit JSON serialisation — returning the object
        // directly does not write it to the HTTP response body.
        if (connectionInfo is null)
        {
            // Surface a clear error if AzureSignalRConnectionString is missing or invalid.
            var err = req.CreateResponse(HttpStatusCode.InternalServerError);
            await err.WriteStringAsync("SignalR connection info is null — check AzureSignalRConnectionString in local.settings.json");
            return err;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        // SignalR JS client requires lowercase property names to detect the Azure redirect.
        await response.WriteAsJsonAsync(new { url = connectionInfo.Url, accessToken = connectionInfo.AccessToken });
        return response;
    }
}
