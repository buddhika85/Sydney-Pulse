// ServiceAlert.cs
// ---------------
// Data payload for the ServiceAlert.v1 CloudEvent published by the Poller Function.
// Shape matches the contract in docs/api.md — any breaking change requires a new version suffix.

/*
 ServiceAlert is the event contract for service disruptions. Examples:

“Significant delays on T1”
“No service between Strathfield and Epping”
“Trackwork on T4 this weekend”
“Bus diversions due to road closure”

The Poller function publishes one ServiceAlert per alert to Event Grid.
Event Grid fans it out to Cosmos DB and the SignalR broadcaster.
 */

namespace SydneyPulse.Core.Events;

public record ServiceAlert(
    string AlertId,
    string RouteShortName,    // user-facing label, e.g. "T1" — also the Cosmos partition key
    string Severity,          // "significant_delays" | "no_service" | "reduced_service" etc.
    string HeaderText,        // short summary shown in the alerts panel
    string DescriptionText,   // full detail text
    DateTimeOffset? StartsAt,
    DateTimeOffset? EndsAt    // null = alert has no defined end time
);
