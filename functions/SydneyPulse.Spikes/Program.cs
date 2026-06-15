// Program.cs
// ----------
// SydneyPulse spike runners. First arg selects the subcommand:
//
//   tfnsw <mode> <topN> [outputPath]    TfNSW vehicle-position discovery
//                                       (story #9 / SP1-16 origin)
//   dlq   <topic> <subscription> <csv>  Service Bus DLQ export to CSV
//                                       (SP1-16 Phase D Alerter investigation)
//
// Each subcommand documents its own setup in the matching SpikeRunner method
// (RunDiscovery below for tfnsw; ExportDlqAsync in DlqExporter.cs for dlq).
//
// Usage (PowerShell):
//
//   # tfnsw discovery (pull API key from KV first)
//   $env:TfNsw__ApiKey = (az keyvault secret show --vault-name sydney-pulse-kv-dev --name TfNswApiKey --query value -o tsv)
//   dotnet run --project . -- tfnsw sydneytrains 5
//   dotnet run --project . -- tfnsw sydneytrains 5 ../SydneyPulse.Tests/Fixtures/sample-trains.json
//
//   # dlq export (uses az login identity)
//   dotnet run --project . -- dlq sydney-pulse-alerts alerter-sub dlq-export-2026-06-16.csv

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SydneyPulse.Core.TfNsw;
using System.Text.Json;

// ── Entry dispatch ───────────────────────────────────────────────────────────
// Top-level statements parse args[0] as the subcommand, the rest go to the
// handler. Unknown subcommand fails fast with a usage line on stderr.

if (args.Length == 0)
{
    Console.Error.WriteLine("usage: dotnet run -- <tfnsw|dlq> [args...]");
    return 1;
}

var subcommand = args[0];
var rest       = args.Skip(1).ToArray();

return subcommand switch
{
    "tfnsw" => await RunTfNswAsync(rest),
    "dlq"   => await SpikeRunner.ExportDlqAsync(rest),
    _       => Unknown(subcommand)
};

// Unknown — print usage and return non-zero so a wrapper script can fail.
static int Unknown(string given)
{
    Console.Error.WriteLine(
        $"unknown subcommand '{given}' — expected 'tfnsw' or 'dlq'");
    return 1;
}

// RunTfNswAsync — preserves the original TfNSW discovery flow from story #9,
// now nested under the `tfnsw` subcommand.
// args[0] = mode (default "sydneytrains"), args[1] = topN (default 5),
// args[2] = optional output path. File gets a JSON array; stdout when omitted.
static async Task<int> RunTfNswAsync(string[] args)
{
    var mode       = args.Length > 0 ? args[0] : "sydneytrains";
    var topN       = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 5;
    var outputPath = args.Length > 2 ? args[2] : null;

    using var host = SpikeRunner.BuildHost();
    var client = host.Services.GetRequiredService<ITfNswFeedClient>();
    await SpikeRunner.RunDiscovery(client, mode, topN, outputPath);
    return 0;
}

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
