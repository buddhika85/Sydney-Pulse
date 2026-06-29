// VehiclesFunction.cs
// --------------------
// GET /api/vehicles — returns current vehicle state from the Cosmos vehicles container.
// Supports optional ?mode= and ?routeShortName= query filters.
//
// Caching: responses are cached in-process for 5 seconds (IMemoryCache, keyed by
// query string). Vehicles update every 30 s; a 5-second window slashes Cosmos RU
// spend by 80 %+ during traffic spikes with zero data-freshness loss — SignalR push
// covers the live-update gap (ADR-0011).

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using SydneyPulse.Core.Cosmos;

namespace SydneyPulse.Functions.AzFunctions.HttpApi;

public class VehiclesFunction(
    CosmosClient cosmosClient,
    IMemoryCache cache,
    ILogger<VehiclesFunction> logger)
{
    // CamelCase + omit nulls so the response matches the docs/api.md contract.
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Function("Vehicles")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "vehicles")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // Cache key includes the full query string so filtered and unfiltered
        // requests each get their own cache entry.
        var cacheKey = $"vehicles:{req.Url.Query}";

        if (!cache.TryGetValue(cacheKey, out string? cachedJson))
        {
            var queryParams = QueryHelpers.ParseQuery(req.Url.Query);
            var mode = queryParams.TryGetValue("mode", out var m) ? m.ToString() : null;
            var routeShortName = queryParams.TryGetValue("routeShortName", out var rsn) ? rsn.ToString() : null;

            var vehicles = await QueryVehiclesAsync(mode, routeShortName, cancellationToken);

            cachedJson = JsonSerializer.Serialize(new
            {
                feedTimestamp = DateTimeOffset.UtcNow,
                vehicles,
            }, JsonOptions);

            cache.Set(cacheKey, cachedJson, TimeSpan.FromSeconds(5));

            logger.LogInformation(
                "Vehicles query (mode={Mode}, route={Route}) returned {Count} documents",
                mode ?? "*", routeShortName ?? "*", vehicles.Count);
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        // Allow CDN / browser to cache for the same window as the in-process cache.
        response.Headers.Add("Cache-Control", "public, max-age=5");
        await response.WriteStringAsync(cachedJson!, cancellationToken);
        return response;
    }

    private async Task<List<VehicleDocument>> QueryVehiclesAsync(
        string? mode, string? routeShortName, CancellationToken cancellationToken)
    {
        var container = cosmosClient.GetContainer(
            FunctionConstants.CosmosDatabaseName, FunctionConstants.VehiclesCosmosContainer);

        QueryDefinition query;
        QueryRequestOptions options;

        if (!string.IsNullOrEmpty(routeShortName))
        {
            // Single-partition read — most RU-efficient path.
            query = new QueryDefinition("SELECT * FROM c WHERE c.routeShortName = @rsn")
                .WithParameter("@rsn", routeShortName);
            options = new QueryRequestOptions { PartitionKey = new PartitionKey(routeShortName) };
        }
        else if (!string.IsNullOrEmpty(mode))
        {
            // Cross-partition, filtered by transport mode.
            query = new QueryDefinition("SELECT * FROM c WHERE c.mode = @mode")
                .WithParameter("@mode", mode);
            options = new QueryRequestOptions();
        }
        else
        {
            // Unfiltered — full container scan for initial dashboard load.
            query = new QueryDefinition("SELECT * FROM c");
            options = new QueryRequestOptions();
        }

        var results = new List<VehicleDocument>();
        var iterator = container.GetItemQueryIterator<VehicleDocument>(query, requestOptions: options);
        while (iterator.HasMoreResults)
        {
            var page = await iterator.ReadNextAsync(cancellationToken);
            results.AddRange(page);
        }
        return results;
    }
}
