// RealtimeService - owns two SignalR HubConnections (vehicles, alerts)
// per SP1-09 design decision B. One connection per hub mirrors the
// backend topology (StateWriterFunction -> "vehicles" hub,
// AlerterFunction -> "alerts" hub). Both hubs broadcast hub-wide;
// groups intentionally not used (Debug Story #20).
//
// Hub + event names live in signalr-events.constants.ts (mirrored from
// the backend's FunctionConstants.cs). No magic strings in this file.
//
// Negotiate flow: client POSTs to /api/negotiate?hub=<name>; SignalR JS client
// handles this internally when withUrl() points at the Function's negotiate
// endpoint (the spike-deployed.html reference from SP1-02 proves the wiring).
/*
SignalR backend hub          this service                   components
  ──────────────────────────────────────────────────────────────────────
  "vehicles" hub  ──push──▶  vehiclesHub.on(...)
                                    │
                                    ▼
                             vehicleUpdates (Subject)  ──as Observable──▶  map/list
                                    ▲
                                    └── .next(payload) called by the handler

  "alerts" hub    ──push──▶  alertsHub.on(...)
                                    │
                                    ▼
                             alertsReceived (Subject)  ──as Observable──▶  banner
*/

import { Injectable } from '@angular/core';
import { HubConnection, HubConnectionBuilder } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { environment } from '../../environments/environment';
import { Alert, Vehicle } from '../models';
import {
  SIGNALR_EVENTS,
  SIGNALR_HUBS,
  SignalRHubName,
} from './signalr-events.constants';

@Injectable({ providedIn: 'root' })
export class RealtimeService {
  // Subjects own the buffer; exposed as Observables so consumers can't push.
  private readonly vehicleUpdates = new Subject<Vehicle>();
  private readonly alertsReceived = new Subject<Alert>();

  // Connections held as nullable - undefined until connect() resolves,
  // null again after disconnect(). Lifecycle owned by this service only.
  private vehiclesHub: HubConnection | null = null;
  private alertsHub: HubConnection | null = null;

  // Public streams - consumers .subscribe() or use toSignal() in components.
  readonly vehicleUpdates$: Observable<Vehicle> =
    this.vehicleUpdates.asObservable();
  readonly alertsReceived$: Observable<Alert> =
    this.alertsReceived.asObservable();

  /**
   * Build, wire, and start both hub connections.
   * Idempotent: safe to call twice (no-op on second call).
   */
  async connect(): Promise<void> {
    // 1 idempotency guard - both hubs already up, nothing to do
    if (this.vehiclesHub && this.alertsHub) {
      return;
    }

    // 2 build locally first; only publish to instance fields after BOTH
    // starts succeed, so a partial failure leaves the service exactly as
    // it was before the call. Prevents the leak path where one hub is
    // connected but the other failed - on retry the next connect() would
    // either skip past the guard (stranding the dead hub) or rebuild on
    // top of the live one (leaking a Free SKU connection slot, cap 20).
    const vehiclesHub = this.buildConnection(SIGNALR_HUBS.Vehicles);
    const alertsHub = this.buildConnection(SIGNALR_HUBS.Alerts);

    // 3 .on() BEFORE .start() so we never miss the first message
    vehiclesHub.on(SIGNALR_EVENTS.VehicleUpdated, (payload: Vehicle) => {
      this.vehicleUpdates.next(payload);
    });
    alertsHub.on(SIGNALR_EVENTS.AlertReceived, (payload: Alert) => {
      this.alertsReceived.next(payload);
    });

    // 4 start both in parallel; on any failure, tear down whatever
    // started (allSettled so a cleanup failure doesn't mask the original
    // error) and re-throw so the caller can retry from a clean slate
    try {
      await Promise.all([vehiclesHub.start(), alertsHub.start()]);
    } catch (err) {
      await Promise.allSettled([vehiclesHub.stop(), alertsHub.stop()]);
      throw err;
    }

    // 5 publish only after both starts succeeded
    this.vehiclesHub = vehiclesHub;
    this.alertsHub = alertsHub;
  }

  /**
   * Stop both hub connections and release references.
   * Idempotent: safe to call when not connected.
   */
  async disconnect(): Promise<void> {
    const stops: Promise<void>[] = [];

    if (this.vehiclesHub) {
      stops.push(this.vehiclesHub.stop());
    }
    if (this.alertsHub) {
      stops.push(this.alertsHub.stop());
    }

    // allSettled (not all): we're tearing down, so a stop() failure
    // must not strand the refs in a non-null state and block future
    // connect()s. Caller doesn't care why a stop failed - the
    // contract that matters is that both refs are null afterwards.
    await Promise.allSettled(stops);

    this.vehiclesHub = null;
    this.alertsHub = null;
  }

  /**
   * Build a HubConnection for one hub name. Caller wires the .on() handler
   * before calling .start() to avoid missing the first message.
   * Uses HubConnectionBuilder + withAutomaticReconnect() for transient
   * disconnect resilience (SignalR Free SKU drops idle connections).
   *
   * `hubName` typed as SignalRHubName so typos fail compile, not runtime.
   */
  private buildConnection(hubName: SignalRHubName): HubConnection {
    return (
      new HubConnectionBuilder()
        // https://..../api/negotiate?hub=vehicles&negotiateVersion=1
        .withUrl(`${environment.apiBaseUrl}?hub=${hubName}`, {
          withCredentials: false,
        })
        .withAutomaticReconnect()
        .build()
    );
  }
}
