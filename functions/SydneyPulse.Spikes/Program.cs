// Program.cs
// ----------
// TfNSW vehicle-position discovery spike.
//
// Purpose: dump the first N VehicleUpdate objects returned by
// TfNswFeedClient.GetVehiclePositionsAsync for a given mode, so we can
// see field-level shape (and spot nulls) without paying for a full cloud
// smoke. Originated from SP1-16 debugging — StateWriter was dropping every
// trains event because VehicleId + RouteShortName were both null, and we
// needed a local feedback loop instead of an AppInsights round-trip.
//
// Usage (PowerShell):

// 1. Set the API key in env vars (pull from Key Vault with az cli).
// $env:TfNsw__ApiKey = (az keyvault secret show --vault-name sydney-pulse-kv-dev --name TfNswApiKey --query value -o tsv)

// 2. Verify
// if ($env: TfNsw__ApiKey) { "key loaded ($($env:TfNsw__ApiKey.Length) chars)" } else { "key NOT set" }

// 3. Run the spike with `dotnet run` in this project folder. Optional args:
//      mode    (default "sydneytrains")
//      topN    (default 5)
//      output  (optional file path — when set, writes valid JSON array; otherwise stdout)
// dotnet run --project . -- sydneytrains 5
// dotnet run --project . -- sydneytrains 5 ../SydneyPulse.Tests/Fixtures/sample-trains-2026-06-11.json

// Output: a JSON array of the first N VehicleUpdate objects. Goes to
// stdout when no output path is given, or to the file when one is.
// The "Fetched X vehicles" summary always goes to stderr so a stdout
// redirect (`> file.json`) captures pure JSON.
// Look at the shape — null/empty VehicleId and/or RouteShortName here
// proves the bug is upstream of StateWriter (in the mapper or the feed),
// not in the Event Grid path.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SydneyPulse.Core.TfNsw;
using System.Text.Json;

// ── Entry point ──────────────────────────────────────────────────────────────
// Top-level statements: parse args, build host, resolve client, run discovery.
// 1. args[0] = mode (default "sydneytrains"), args[1] = topN (default 5),
//    args[2] = optional output path (file gets a JSON array; stdout when omitted).
var mode       = args.Length > 0 ? args[0] : "sydneytrains";
var topN       = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 5;
var outputPath = args.Length > 2 ? args[2] : null;

// 2. Build host + pull the singleton TfNswFeedClient out of DI.
using var host = SpikeRunner.BuildHost();
var client = host.Services.GetRequiredService<ITfNswFeedClient>();

// 3. Run discovery — top-level await is allowed in top-level statements.
await SpikeRunner.RunDiscovery(client, mode, topN, outputPath);

return;

// ── Helpers ──────────────────────────────────────────────────────────────────

internal static partial class SpikeRunner
{
    // BuildHost: wires the same DI registrations the Functions worker uses
    // for TfNsw (named HttpClient with Polly resilience + Options binding +
    // singleton client). Reads config from env vars so the API key can be
    // injected via `$env:TfNsw__ApiKey` without a settings file.
    //
    // Returns a built IHost — caller resolves services via host.Services.
    public static IHost BuildHost()
    {        
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<TfNswOptions>(
             builder.Configuration.GetSection(TfNswOptions.SectionName));
        builder.Services.AddHttpClient("TfNsw").AddStandardResilienceHandler();
        builder.Services.AddSingleton<ITfNswFeedClient, TfNswFeedClient>();
        return builder.Build();
    }

    // RunDiscovery: calls the live feed for `mode`, takes the first `topN`
    // results, and emits them as a single indented JSON array. Pure observation
    // — no mutation, no Event Grid publish, no Cosmos write. Safe to run
    // repeatedly.
    //
    // outputPath:
    //   null  → JSON array goes to stdout; summary line to stderr
    //   path  → JSON array written to the file; summary line to stderr
    // Summary always goes to stderr so `dotnet run ... > file.json` produces
    // valid JSON without needing the explicit path arg.
    //
    // What to look for in the output:
    //   - VehicleId populated? (TfNSW trains feed often empty)
    //   - RouteShortName populated? (relies on GTFS static enrichment)
    //   - Latitude / Longitude in the right range for Sydney (~-33.8, ~151.2)
    //   - Mode field matches what was requested
    public static async Task RunDiscovery(
        ITfNswFeedClient client,
        string mode,
        int topN,
        string? outputPath = null,
        CancellationToken cancellationToken = default)
    {
        // 1. Fetch live data and pick the first N for inspection.
        var vehicles = await client.GetVehiclePositionsAsync(mode, cancellationToken);
        var sample   = vehicles.Take(topN).ToList();

        // 2. Human-readable summary on stderr so stdout stays pure JSON
        //    (lets `> file.json` redirects produce valid JSON without -- arg).
        Console.Error.WriteLine($"Fetched {vehicles.Count} vehicles for mode '{mode}'.");

        // 3. Serialize the whole sample as ONE array — valid JSON either way.
        var json = JsonSerializer.Serialize(sample, new JsonSerializerOptions { WriteIndented = true });

        // 4. Route to file or stdout depending on whether a path was supplied.
        if (outputPath is null)
        {
            Console.WriteLine(json);
        }
        else
        {
            await File.WriteAllTextAsync(outputPath, json, cancellationToken);
            Console.Error.WriteLine($"Wrote {sample.Count} entries to {outputPath}.");
        }
    }
}
