// RouteInfo.cs
// ------------
// Immutable snapshot of a single GTFS static route, parsed from routes.txt.
// Cached in-memory inside TfNswFeedClient for 1 hour (ADR-0009).
// Used to enrich realtime vehicle positions with human-readable names and colours.

namespace SydneyPulse.Core.TfNsw;

public record RouteInfo(
    string RouteId,       // internal GTFS ID, e.g. "NTH_1a"
    string ShortName,     // user-facing label, e.g. "T1"
    string LongName,      // e.g. "North Shore & Western Line"
    string Color,         // hex with leading #, e.g. "#F99D1C"
    string Mode           // transport mode key, e.g. "sydneytrains"
);
