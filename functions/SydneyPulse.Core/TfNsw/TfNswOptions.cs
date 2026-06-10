// TfNswOptions.cs
// ---------------
// Strongly-typed config for the TfNSW Open Data API, bound via IOptions<TfNswOptions>.
// Registered in Program.cs: services.Configure<TfNswOptions>(config.GetSection(TfNswOptions.SectionName))

/*
 TfNswOptions is a strongly‑typed configuration class that holds the API key, base URL, and transport modes 
for the TfNSW Open Data API. It is bound using the OPTIONS PATTERN, populated from Key Vault in Azure, 
and injected into the Poller Function so it knows which GTFS‑Realtime feeds to poll.
 */

namespace SydneyPulse.Core.TfNsw;

public class TfNswOptions
{
    public const string SectionName = "TfNsw";

    // API key sourced from Key Vault via Managed Identity in the cloud;
    // set as an environment variable locally.
    public string ApiKey { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = "https://api.transport.nsw.gov.au";

    // Transport modes polled on each timer tick (Poller Function, SP1-05).
    // TODO(sprint-bus-expansion): add buses. TfNSW splits bus GTFS-RT feeds per
    // operator network (SMBSC001..SMBSC014 — Sydney Metropolitan Bus Service
    // Contracts). The flat `/v2/gtfs/vehiclepos/buses` URL 404s; bus support
    // requires iterating operator codes (e.g. "buses/SMBSC001") AND a polling
    // cadence redesign because 14 operators × 2 endpoints × 30s ticks blows
    // through TfNSW's 60k/day quota. See `docs/sprints/sprint-bus-expansion.md`
    // for the full surface area + rate-limit math.
    // SP1-16 deliberately scoped to sydneytrains only to prove the pipeline
    // end-to-end with one flat-feed mode.
    public string[] VehicleModes { get; set; } = ["sydneytrains"];
}
