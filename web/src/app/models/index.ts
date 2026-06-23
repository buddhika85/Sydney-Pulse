// Barrel re-export for the models folder. Consumers:
//   import { Vehicle, Alert, TransportRoute } from '../../models';
// `export type` is used because the entries are pure interfaces -
// keeps the emitted JS clean under `isolatedModules`.

export type { Vehicle, VehiclesResponse } from './vehicle';
export type { Alert, AlertsResponse } from './alert';
export type {
  TransportRoute,
  TransportRoutesResponse,
} from './transport-route';
