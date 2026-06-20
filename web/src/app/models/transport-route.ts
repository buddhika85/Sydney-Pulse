// TransportRoute - static GTFS route metadata (name, color, mode).
// Named TransportRoute deliberately to avoid collision with the
// Angular Router's own `Route` type.
// Shape derived from the GET /api/routes HTTP API response.
// Authoritative fixture for property derivation:
//   functions/SydneyPulse.Tests/Fixtures/routes-2026-06-17.json

export interface TransportRoute {
  routeId: string;
  routeShortName: string;
  routeLongName: string;
  routeColor: string;
  mode: string;
}

export interface TransportRoutesResponse {
  routes: TransportRoute[];
}
