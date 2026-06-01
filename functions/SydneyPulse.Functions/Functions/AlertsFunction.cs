// AlertsFunction.cs
// ------------------
// GET /api/alerts — returns currently active service alerts from the Cosmos
// alerts container. No filters needed; the container holds only live alerts
// (TTL 24 h purges expired ones automatically).

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Cosmos;

namespace SydneyPulse.Functions.Functions;

public class AlertsFunction(
    CosmosClient cosmosClient,
    ILogger<AlertsFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Function("Alerts")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "alerts")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        var container = cosmosClient.GetContainer(
            FunctionConstants.CosmosDatabaseName, FunctionConstants.AlertsCosmosContainer);

        // Cross-partition scan — alerts are partitioned by routeShortName and there
        // is no use case for returning alerts for a single route in isolation.
        var results  = new List<AlertDocument>();
        var iterator = container.GetItemQueryIterator<AlertDocument>(
            new QueryDefinition("SELECT * FROM c ORDER BY c.receivedAt DESC"));

        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }

        logger.LogInformation("Alerts query returned {Count} documents", results.Count);

        var json = JsonSerializer.Serialize(new { alerts = results }, JsonOptions);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(json, cancellationToken);
        return response;
    }
}
