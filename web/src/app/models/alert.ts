// Alert - active TfNSW service alert affecting one or more routes.
// Shape derived from the GET /api/alerts HTTP API response.
// Authoritative fixture for property derivation:
//   functions/SydneyPulse.Tests/Fixtures/alerts-2026-06-17.json
// Backend/frontend drift surfaces here as a type error first.

export interface Alert {
  id: string;
  routeShortName: string;
  alertId: string;

  severity: string;
  headerText: string;
  descriptionText: string;

  /** convert at usage site via `new Date(startsAt)` if not null. */
  startsAt?: string;

  /** convert at usage site via `new Date(endsAt)` if not null. */
  endsAt?: string;

  /** convert at usage site via `new Date(receivedAt)`. */
  receivedAt: string;
}

export interface AlertsResponse {
  alerts: Alert[];
}
