// SpikeFunction
// -------------
// De-risk spike only — not part of the production pipeline.
// Accepts an anonymous HTTP POST and broadcasts a fixed hello message
// to all clients connected to the "spike" SignalR hub.
//
// Usage: curl -X POST http://localhost:7071/api/spike
// Expected result: every browser with spike.html open receives {"text":"hello"}

using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.SignalRService;

namespace SydneyPulse.Functions.Functions;

public class SpikeFunction
{
    [Function("spike")]
    // SignalROutput wires the return value directly to the hub — no SDK client needed.
    [SignalROutput(HubName = "spike")]
    public SignalRMessageAction Run(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post")] HttpRequestData req)
    {
        // Broadcast to all connected clients; target matches the on("newMessage") listener in spike.html.
        return new SignalRMessageAction("newMessage", [new { text = "hello" }]);
    }
}
