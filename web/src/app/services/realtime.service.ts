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

import { Injectable, inject } from '@angular/core';
import { HubConnection } from '@microsoft/signalr';
import { Observable, Subject } from 'rxjs';

import { environment } from '../../environments/environment';
import { Alert, Vehicle } from '../models';
import { SIGNALR_EVENTS, SIGNALR_HUBS, SignalRHubName } from './signalr-events.constants';

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
  readonly vehicleUpdates$: Observable<Vehicle> = this.vehicleUpdates.asObservable();
  readonly alertsReceived$: Observable<Alert> = this.alertsReceived.asObservable();

  /**
   * Build, wire, and start both hub connections.
   * Idempotent: safe to call twice (no-op on second call).
   *
   * Estimate: ~20 min - guard if already connected, call buildConnection
   * for SIGNALR_HUBS.Vehicles and SIGNALR_HUBS.Alerts, wire .on() with
   * SIGNALR_EVENTS.VehicleUpdated / .AlertReceived into their Subjects,
   * await both .start() in parallel.
   */
  async connect(): Promise<void> {
    // TODO(SP1-09 step 5): implement.
    throw new Error('Not implemented');
  }

  /**
   * Stop both hub connections and release references.
   * Idempotent: safe to call when not connected.
   *
   * Estimate: ~10 min - if hub non-null, await .stop(); set both to null.
   * Subjects are NOT completed (service is a singleton; future connect()
   * calls must keep emitting to the same Subjects).
   */
  async disconnect(): Promise<void> {
    // TODO(SP1-09 step 5): implement.
    throw new Error('Not implemented');
  }

  /**
   * Build a HubConnection for one hub name. Caller wires the .on() handler
   * before calling .start() to avoid missing the first message.
   * Uses HubConnectionBuilder + withAutomaticReconnect() for transient
   * disconnect resilience (SignalR Free SKU drops idle connections).
   *
   * `hubName` typed as SignalRHubName so typos fail compile, not runtime.
   *
   * Estimate: ~15 min - new HubConnectionBuilder().withUrl(negotiateUrl)
   *                     .withAutomaticReconnect().build().
   * negotiateUrl shape: `${environment.apiBaseUrl}/negotiate?hub=<hubName>`.
   */
  private buildConnection(hubName: SignalRHubName): HubConnection {
    // TODO(SP1-09 step 5): implement.
    throw new Error('Not implemented');
  }
}
