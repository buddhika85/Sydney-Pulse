// VehicleUpdate.cs
// ----------------
// Data payload for the VehicleUpdate.v1 CloudEvent published by the Poller Function
// to Azure Event Grid to Fan out to Downstream consumers.

// Flow
// Every 30 seconds, the Poller Function:
// Downloads the .pb file from TfNSW
// Parses it using the generated Protobuf classes
// For Each Vehicle
// Extracts the fields we care about and Maps to VehicleUpdate 
// Publish to Event Grid

// Shape matches the contract in docs/api.md — any breaking change requires a new version suffix.

namespace SydneyPulse.Core.Events;

public record VehicleUpdate(
    string VehicleId,
    string TripId,
    string RouteId,
    string RouteShortName,    // user-facing label, e.g. "T1" — also the Cosmos partition key
    string RouteLongName,
    string RouteColor,        // hex with leading #, e.g. "#F99D1C"
    string Mode,              // "sydneytrains" | "buses" | "ferries" | "metro" | "lightrail"
    double Latitude,
    double Longitude,
    float? Bearing,
    float? SpeedKmh,
    string? OccupancyStatus,
    DateTimeOffset VehicleTimestamp
);
