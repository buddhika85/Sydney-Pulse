// VehicleUpdateCloudEventRoundTripTests.cs
// ----------------------------------------
// Diagnostic + regression for SP1-16 production symptom:
// StateWriter received VehicleUpdate with vehicleId='(null)' and
// routeShortName='(null)' on every event — but the discovery spike
// (functions/SydneyPulse.Spikes) proved TfNswFeedClient produces fully
// populated records locally (see Fixtures/sample-trains-2026-06-11.json).
//
// That isolates the bug to the Poller → Event Grid → StateWriter
// serialization round-trip. These tests reproduce that round-trip in
// pure .NET (no Azure call) so we can pin the failure to a specific layer:
//
//   - If the round-trip test FAILS, the bug is in CloudEvent SDK serialization
//     OR System.Text.Json default options — fixable with a [JsonPropertyName]
//     attribute or a custom serializer in PollerFunction.
//   - If the round-trip test PASSES, the bug is in the Event Grid wire path
//     or Worker EG extension binding — needs deeper inspection (e.g. swap
//     StateWriter param type to CloudEvent and dump raw Data bytes).
//
// Either way, this file becomes a permanent test asset.

using Azure.Messaging;
using SydneyPulse.Core.Events;
using System.Text.Json;
using Xunit;

namespace SydneyPulse.Tests.Unit.Events;

public class VehicleUpdateCloudEventRoundTripTests
{
    // GIVEN a VehicleUpdate with every field populated (real trains entry
    //       from Fixtures/sample-trains-2026-06-11.json[0])
    // WHEN  wrapped in a CloudEvent (matching PollerFunction line ~56) and
    //       Data is deserialized back to VehicleUpdate (matching what the
    //       Worker EG extension does for [EventGridTrigger] VehicleUpdate)
    // THEN  every field should round-trip with the original value
    [Fact]
    public void CloudEvent_round_trip_preserves_VehicleUpdate_fields()
    {
        // 1. Arrange — known-good record from the live spike output.
        var original = new VehicleUpdate(
            VehicleId: "4020.9909.3191.8154.4042.7685.2060.9836",
            TripId: "611H.1287.134.48.T.8.89913364",
            RouteId: "ESI_2a",
            RouteShortName: "T4",
            RouteLongName: "Waterfall to Bondi Junction via City",
            RouteColor: "#005AA3",
            Mode: "sydneytrains",
            Latitude: -33.874778747558594,
            Longitude: 151.2223358154297,
            Bearing: 0f,
            SpeedKmh: 0f,
            OccupancyStatus: "Empty",
            VehicleTimestamp: DateTimeOffset.Parse("2026-06-11T02:55:08+00:00"));

        // 2. Act — publish-side: same constructor PollerFunction uses.
        //    The Azure SDK serializes `original` into CloudEvent.Data using
        //    JsonObjectSerializer.Default (System.Text.Json with default opts).
        var ce = new CloudEvent(
            source: "/sydney-pulse/poller",
            type: "com.sydneypulse.VehicleUpdate.v1",
            jsonSerializableData: original);

        // 3. Receive-side: same shape the Worker EG extension uses to
        //    materialise [EventGridTrigger] VehicleUpdate — pulls Data out
        //    as BinaryData and deserializes via default System.Text.Json.
        var roundTripped = ce.Data!.ToObjectFromJson<VehicleUpdate>();

        // 4. Assert — every field must survive the round-trip.
        Assert.NotNull(roundTripped);
        Assert.Equal(original.VehicleId, roundTripped!.VehicleId);
        Assert.Equal(original.RouteShortName, roundTripped.RouteShortName);
        Assert.Equal(original.TripId, roundTripped.TripId);
        Assert.Equal(original.RouteId, roundTripped.RouteId);
        Assert.Equal(original.RouteLongName, roundTripped.RouteLongName);
        Assert.Equal(original.RouteColor, roundTripped.RouteColor);
        Assert.Equal(original.Mode, roundTripped.Mode);
        Assert.Equal(original.Latitude, roundTripped.Latitude);
        Assert.Equal(original.Longitude, roundTripped.Longitude);
        Assert.Equal(original.OccupancyStatus, roundTripped.OccupancyStatus);
        Assert.Equal(original.VehicleTimestamp, roundTripped.VehicleTimestamp);
    }

    // Inspection-only — proves what casing the Azure SDK puts on the wire.
    // If this FAILS (i.e. SDK emits camelCase), the production bug is a
    // casing mismatch: publisher writes camelCase, subscriber deserializes
    // with default case-sensitive System.Text.Json → all PascalCase target
    // properties end up null. That's the SP1-16 symptom exactly.
    [Fact]
    public void CloudEvent_Data_serializes_as_PascalCase()
    {
        // 1. Arrange — minimal record; values don't matter, only the keys do.
        var v = new VehicleUpdate(
            VehicleId: "V1", TripId: "T1", RouteId: "R1",
            RouteShortName: "RSN1", RouteLongName: "RLN1", RouteColor: "#FFF",
            Mode: "trains", Latitude: -33.8, Longitude: 151.2,
            Bearing: 0f, SpeedKmh: 0f, OccupancyStatus: "Empty",
            VehicleTimestamp: DateTimeOffset.UnixEpoch);

        // 2. Act — same SDK call PollerFunction makes.
        var ce = new CloudEvent("/test", "com.sydneypulse.VehicleUpdate.v1", jsonSerializableData: v);
        var json = ce.Data!.ToString();

        // 3. Assert — PascalCase keys must be present on the wire.
        Assert.Contains("\"VehicleId\"", json);
        Assert.Contains("\"RouteShortName\"", json);
    }
}
