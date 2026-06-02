// RoutesFunction.cs
// ------------------
// GET /api/routes — returns GTFS static route metadata for all configured
// transport modes. Data is served from TfNswFeedClient's in-process 1-hour
// cache (ADR-0009); this endpoint costs zero Cosmos RUs.

using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.TfNsw;

namespace SydneyPulse.Functions.AzFunctions.HttpApi;

public class RoutesFunction(
    ITfNswFeedClient feedClient,
    IOptions<TfNswOptions> tfNswOptions,
    ILogger<RoutesFunction> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    [Function("Routes")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "routes")] HttpRequestData req,
        CancellationToken cancellationToken)
    {
        // Fetch routes for every configured mode and flatten into one list.
        // TfNswFeedClient returns from its 1-hour in-memory cache on warm calls.
        var allRoutes = new List<RouteInfo>();
        foreach (var mode in tfNswOptions.Value.VehicleModes)
        {
            var routes = await feedClient.GetRoutesAsync(mode, cancellationToken);
            allRoutes.AddRange(routes.Values);
        }

        // Project RouteInfo fields to match the docs/api.md response contract.
        // RouteInfo uses short field names (ShortName, LongName, Color) while the
        // API contract uses qualified names (routeShortName, routeLongName, routeColor).
        var projected = allRoutes.Select(r => new
        {
            routeId        = r.RouteId,
            routeShortName = r.ShortName,
            routeLongName  = r.LongName,
            routeColor     = r.Color,
            mode           = r.Mode,
        });

        logger.LogInformation("Routes query returned {Count} routes across {Modes} modes",
            allRoutes.Count, tfNswOptions.Value.VehicleModes.Length);

        // Use explicit serialization (not WriteAsJsonAsync) to avoid a DI dependency
        // on IObjectSerializer that is unavailable outside the Functions host.
        var json = JsonSerializer.Serialize(new { routes = projected }, JsonOptions);
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(json, cancellationToken);
        return response;
    }
}
