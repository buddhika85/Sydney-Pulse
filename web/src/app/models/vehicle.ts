// Vehicle - current real-time state of a transport vehicle.
// Shape derived from the GET /api/vehicles HTTP API response.
// Authoritative fixture for property derivation:
//   functions/SydneyPulse.Tests/Fixtures/vehicles-T1-2026-06-17.json
// Backend/frontend drift surfaces here as a type error first.

export interface Vehicle {
  id: string;
  routeShortName: string;
  vehicleId: string;
  latitude: number;
  longitude: number;
  bearing?: number;
  speedKmh?: number;
  routeId: string;
  routeLongName: string;
  routeColor: string;
  mode: string;
  occupancyStatus?: string;

  /** convert at usage site via `new Date(timestamp)`. */
  timestamp: string;

  /** when we wrote the doc to Cosmos. */
  updatedAt: string;
}

export interface VehiclesResponse {
  /** when the backend's last GTFS poll completed. */
  feedTimestamp: string;
  vehicles: Vehicle[];
}
