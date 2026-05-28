# API contracts

HTTP endpoints exposed by the API Function and SignalR event payloads
consumed by the Angular frontend.

## Base URL

- dev: `https://sydney-pulse-func-dev.azurewebsites.net/api`
- prod: `https://sydney-pulse-func-prod.azurewebsites.net/api`

CORS is configured to allow the Static Web App origin only.

## HTTP endpoints

### `GET /api/vehicles`

Returns current state of all vehicles, optionally filtered by mode.

Query parameters:

- `mode` (optional) — one of `trains`, `buses`, `ferries`, `metro`, `lightrail`
- `routeShortName` (optional) — e.g. `T1`, `333`, `M1`

Response (200):

```json
{
  "feedTimestamp": "2026-05-28T14:32:11+10:00",
  "vehicles": [
    {
      "vehicleId": "2001.3133.2777.4998.8510.3377.1575.4644",
      "routeId": "NTH_1a",
      "routeShortName": "T1",
      "routeLongName": "North Shore & Western Line",
      "routeColor": "#F99D1C",
      "mode": "trains",
      "latitude": -33.8378,
      "longitude": 151.1973,
      "speedKmh": 0,
      "status": "stoppedAt",
      "stopName": "Hornsby Station",
      "occupancy": "fewSeatsAvailable",
      "carriages": 8,
      "timestamp": "2026-05-28T14:32:08+10:00"
    }
  ]
}
```

### `GET /api/alerts`

Returns currently active service alerts.

Response (200):

```json
{
  "alerts": [
    {
      "alertId": "alert-T1-20260528-001",
      "routeShortName": "T1",
      "routeColor": "#F99D1C",
      "severity": "delay",
      "headerText": "T1 Western Line",
      "descriptionText": "8 min delays — signal fault near Strathfield",
      "startsAt": "2026-05-28T14:25:00+10:00",
      "endsAt": null,
      "updatedAt": "2026-05-28T14:30:12+10:00"
    }
  ]
}
```

### `GET /api/routes`

Returns route metadata (cached in-memory, refreshed hourly).

Response (200):

```json
{
  "routes": [
    {
      "routeId": "NTH_1a",
      "routeShortName": "T1",
      "routeLongName": "North Shore & Western Line",
      "routeColor": "#F99D1C",
      "mode": "trains"
    }
  ]
}
```

### `GET /api/analytics/reliability`

Query parameters:

- `routeShortName` (required)
- `days` (default 30, max 90)

Response (200):

```json
{
  "routeShortName": "T1",
  "windowDays": 30,
  "onTimePercent": 87.0,
  "averageDelayMinutes": 3.2,
  "incidentCount": 42,
  "worstHourLocal": 8,
  "heatmap": [
    {"dayOfWeek": 1, "hourLocal": 8, "onTimePercent": 72.0},
    {"dayOfWeek": 1, "hourLocal": 9, "onTimePercent": 81.0}
  ]
}
```

Backed by a Kusto query against Application Insights traces.

### `GET /api/ops/slos`

Returns current SLO values for the operations dashboard.

Response (200):

```json
{
  "apiAvailability": {"target": 99.9, "actual": 99.94, "status": "healthy"},
  "eventToPushP95Ms": {"target": 5000, "actual": 1200, "status": "healthy"},
  "errorBudgetRemainingPercent": 73,
  "dlqDepth": {"threshold": 10, "actual": 0, "status": "healthy"}
}
```

### `POST /api/negotiate`

SignalR connection token endpoint. Called by the Angular client before
establishing the WebSocket.

Response (200):

```json
{
  "url": "https://sydney-pulse-signalr-prod.service.signalr.net/client/...",
  "accessToken": "eyJhbGc..."
}
```

Token TTL: 1 hour. Anonymous (no user identity).

## SignalR events

The Angular client subscribes to two groups after negotiate.

### `vehicles` group

Event name: `VehicleUpdated`

Payload (one per affected vehicle):

```json
{
  "vehicleId": "2001.3133...",
  "routeShortName": "T1",
  "routeColor": "#F99D1C",
  "mode": "trains",
  "latitude": -33.8378,
  "longitude": 151.1973,
  "speedKmh": 12,
  "timestamp": "2026-05-28T14:32:08+10:00"
}
```

The frontend deduplicates by `vehicleId` and updates the map marker. If
the timestamp is older than what's currently displayed, the update is
dropped (stale event).

### `alerts` group

Event name: `AlertPublished`

Payload (same shape as `/api/alerts` items).

Alerts are deduplicated by `alertId`. Re-publishes for the same alert
ID update the existing card; new alert IDs add a card to the top of
the panel.

## Event Grid event schemas

Published by the Poller, consumed by State Writer / Archiver / (via
Service Bus subscription filter) Alerter chain.

### `VehicleUpdate.v1`

```json
{
  "id": "guid",
  "source": "/sydney-pulse/poller",
  "type": "VehicleUpdate.v1",
  "datacontenttype": "application/json",
  "time": "2026-05-28T14:32:11+10:00",
  "data": {
    "vehicleId": "string",
    "routeId": "string",
    "routeShortName": "string",
    "mode": "trains|buses|ferries|metro|lightrail",
    "latitude": -33.8378,
    "longitude": 151.1973,
    "speedKmh": 12,
    "occupancyStatus": "string",
    "vehicleTimestamp": "2026-05-28T14:32:08+10:00"
  }
}
```

### `ServiceAlert.v1`

```json
{
  "id": "guid",
  "source": "/sydney-pulse/poller",
  "type": "ServiceAlert.v1",
  "datacontenttype": "application/json",
  "time": "2026-05-28T14:32:11+10:00",
  "data": {
    "alertId": "string",
    "routeShortName": "string",
    "severity": "delay|disruption|info",
    "headerText": "string",
    "descriptionText": "string",
    "startsAt": "2026-05-28T14:25:00+10:00",
    "endsAt": "2026-05-28T16:00:00+10:00"
  }
}
```

## Versioning

Event types are versioned in their type name (`.v1`). Breaking changes
require a new version. Consumers handle multiple versions simultaneously
during transitions.
