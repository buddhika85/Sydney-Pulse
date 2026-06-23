// signalr-events.constants.ts - SignalR hub + event-name contract.
// Mirrors the backend's FunctionConstants.cs (no-magic-strings rule).
// Drift between this file and FunctionConstants.cs = silent dropped
// messages on the client (Debug Story #20 ★ cluster). Treat as sealed.
//
// Backend source of truth:
//   functions/SydneyPulse.Functions/FunctionConstants.cs
//     VehiclesSignalRHub           = "vehicles"
//     AlertsSignalRHub             = "alerts"
//     VehicleUpdatedSignalREvent   = "vehicleUpdated"
//     AlertReceivedSignalREvent    = "alertReceived"

// Hub name = path segment in /api/negotiate?hub=<name>.
export const SIGNALR_HUBS = {
  Vehicles: 'vehicles',
  Alerts: 'alerts',
} as const;

// Event name = first arg of HubConnection.on(eventName, handler).
export const SIGNALR_EVENTS = {
  VehicleUpdated: 'vehicleUpdated',
  AlertReceived: 'alertReceived',
} as const;

// Union of valid hub names - used by RealtimeService.buildConnection to
// reject typos at compile time rather than at run time (dropped messages).
export type SignalRHubName = (typeof SIGNALR_HUBS)[keyof typeof SIGNALR_HUBS];
