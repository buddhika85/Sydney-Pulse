// live.component.ts - the commuter-facing live dashboard container.
//
// Wires together the Leaflet map (ADR-0014: raw Leaflet, no wrapper), the
// SignalR streams (ADR-0013: initial snapshot then trust the stream, no
// periodic HTTP refetch), the alerts rail, and the freshness badge.
//
// Composition (SP1-10 Phase 1 lock):
//   VehiclesService.getVehicles()    -> initial map snapshot + feedTimestamp
//   AlertsService.getAlerts()        -> initial alerts rail
//   RealtimeService.vehicleUpdates$  -> marker upserts + latestStreamTs
//   RealtimeService.alertsReceived$  -> alerts prepended to rail
//   5s interval                      -> freshness re-eval + stale marker prune
//
// State model: signals for anything the template reads, computed for
// derivations, RxJS only for the SignalR streams themselves. Matches the
// signal-first pattern set by SP1-09.
//
// The map object + marker cache are NOT signals - they are imperative
// Leaflet handles owned by this component's lifecycle, mutated in place.
// Wrapping them in signals would trigger change detection for no gain.

import {
  AfterViewInit,
  ChangeDetectionStrategy,
  Component,
  DestroyRef,
  ElementRef,
  OnDestroy,
  computed,
  inject,
  signal,
  viewChild,
} from '@angular/core';
import * as L from 'leaflet';

import { takeUntilDestroyed } from '@angular/core/rxjs-interop';

import { Alert, Vehicle } from '../../models';
import { AlertsService } from '../../services/alerts.service';
import { RealtimeService } from '../../services/realtime.service';
import { VehiclesService } from '../../services/vehicles.service';
import { AlertsPanelComponent } from './alerts-panel/alerts-panel.component';
import { FiltersBarComponent } from './filters-bar/filters-bar.component';
import { computeLatestEventTimestamp, isStale } from './freshness.util';
import { MarkerEntry, pruneStale, upsertMarker } from './vehicle-marker';
import {
  DEFAULT_MAP_ZOOM,
  FRESHNESS_RE_EVAL_INTERVAL_MS,
  SYDNEY_CBD_LAT,
  SYDNEY_CBD_LNG,
  VEHICLE_MARKER_TTL_MS,
} from '../../shared/design-tokens';
import { firstValueFrom } from 'rxjs';

@Component({
  selector: 'sp-live',
  standalone: true,
  imports: [FiltersBarComponent, AlertsPanelComponent],
  templateUrl: './live.component.html',
  styleUrl: './live.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveComponent implements AfterViewInit, OnDestroy {
  // ---- collaborators ----
  private readonly realtime = inject(RealtimeService);
  private readonly vehiclesService = inject(VehiclesService);
  private readonly alertsService = inject(AlertsService);
  private readonly destroyRef = inject(DestroyRef);

  // Leaflet mount point - resolved in ngAfterViewInit, not the constructor.
  readonly mapEl = viewChild<ElementRef<HTMLDivElement>>('mapEl');

  // ---- signals (template-reactive state) ----
  // null = "All routes" - shared filter contract with FiltersBarComponent
  readonly selectedRoute = signal<string | null>(null);

  // canonical vehicle list: fetch-seeded, then upserted from the stream
  readonly vehicles = signal<Vehicle[]>([]);
  readonly alerts = signal<Alert[]>([]);

  // freshness inputs - one from the initial fetch, one rolling from the stream
  readonly feedTimestamp = signal<string | null>(null);
  readonly latestStreamTs = signal<string | null>(null);

  // bumped every FRESHNESS_RE_EVAL_INTERVAL_MS so isStale() recomputes
  readonly freshnessNow = signal<number>(Date.now());

  // ---- computeds ----
  readonly isStale = computed<boolean>(() => {
    const latest = computeLatestEventTimestamp(
      this.feedTimestamp(),
      this.latestStreamTs(),
    );
    return isStale(latest, this.freshnessNow());
  });

  // distinct sorted routeShortName list - sourced from vehicles, NOT
  // /api/routes (Phase 1 lock: the static catalogue is ~200 mostly-empty)
  readonly routeOptions = computed<string[]>(() => {
    const set = new Set(this.vehicles().map((v) => v.routeShortName));
    return [...set].sort();
  });

  readonly filteredAlerts = computed<Alert[]>(() => {
    const route = this.selectedRoute();
    return route
      ? this.alerts().filter((a) => a.routeShortName === route)
      : this.alerts();
  });

  // ---- imperative handles (owned by lifecycle, not signalised) ----
  private map: L.Map | null = null;
  private readonly markers = new Map<string, MarkerEntry>();
  private freshnessIntervalId?: number;

  /**
   * Lifecycle entry. Sequence matters: init the map before we upsert
   * markers, load the initial snapshot before we connect the stream
   * (ADR-0013), start the freshness timer only after both.
   */
  async ngAfterViewInit(): Promise<void> {
    const mapDomElement = this.mapEl();
    if (!mapDomElement)
      throw new Error('mapEl viewChild did not resolve by ngAfterViewInit');
    this.initMap(mapDomElement.nativeElement);

    // LEARN: load vehicles + alerts in parallel
    await Promise.all([this.loadInitialVehicles(), this.loadInitialAlerts()]);

    await this.startRealtime();
    this.startFreshnessTimer();
  }

  /**
   * Cleanup. All three handles need explicit teardown - the map holds
   * DOM + tile requests, the interval keeps a reference alive, and
   * RealtimeService.disconnect() releases the two SignalR Free SKU
   * connection slots (cap 20).
   */
  ngOnDestroy(): void {
    if (this.freshnessIntervalId) clearInterval(this.freshnessIntervalId); // 5 second freshness interval stops
    this.map?.remove();
    this.map = null;
    this.markers.clear();
    this.realtime.disconnect();
  }

  /**
   * Creates the Leaflet map on the DOM mount point and adds the OSM tile
   * layer. Runs once from ngAfterViewInit; the map handle is then owned
   * by this component's lifecycle until ngOnDestroy tears it down
   */
  private initMap(el: HTMLElement): void {
    this.map = L.map(el, {
      center: [SYDNEY_CBD_LAT, SYDNEY_CBD_LNG],
      zoom: DEFAULT_MAP_ZOOM,
    });

    // LEARN: {z}=zoom, {x}/{y}=tile column/row - Leaflet fills them from the viewport
    L.tileLayer('https://tile.openstreetmap.org/{z}/{x}/{y}.png', {
      attribution: '&copy; OpenStreetMap contributors',
    }).addTo(this.map);
  }

  /**
   * Seeds the map + vehicles signal + feedTimestamp from a one-shot HTTP
   * fetch. Runs before startRealtime() so the map is populated the moment
   * SignalR starts pushing updates (ADR-0013 initial-snapshot-then-stream).
   */
  private async loadInitialVehicles(): Promise<void> {
    const vehicleEnvelope = await firstValueFrom(
      this.vehiclesService.getVehicles(),
    );
    this.feedTimestamp.set(vehicleEnvelope.feedTimestamp);

    // filter out vehicles missing position before setting signal + plotting
    const vehiclesWithPositions = vehicleEnvelope.vehicles.filter(
      (vehicle) => vehicle.longitude != null && vehicle.latitude != null,
    );
    this.vehicles.set(vehiclesWithPositions);
    vehiclesWithPositions.forEach((vehicle) =>
      upsertMarker(this.map!, this.markers, vehicle),
    );
  }

  /**
   * Seeds the alerts signal from a one-shot HTTP fetch. Runs before the
   * SignalR alerts stream connects so the panel isn't empty during the
   * negotiate handshake.
   */
  private async loadInitialAlerts(): Promise<void> {
    // LEARN: firstValueFrom()
    // turns an Observable into a Promise
    // waits for the first value
    // auto-unsubscribes - unlike subscribe which requires manual unsubscribes
    const alerts = await firstValueFrom(this.alertsService.getAlerts());
    this.alerts.set(alerts);
  }

  /**
   * Connects RealtimeService then wires both SignalR streams. Sits after
   * the initial HTTP fetches (ADR-0013 initial-snapshot-then-stream) and
   * before the freshness timer, which needs the streams live to have a
   * meaningful latestStreamTs to track.
   */
  private async startRealtime(): Promise<void> {
    await this.realtime.connect();
    this.wireVehicleStream();
    this.wireAlertStream();
  }

  /**
   * Forwards each SignalR vehicle push into three places: the map (via
   * upsertMarker), the vehicles signal (upsert by vehicleId), and the
   * latestStreamTs signal (rolling max for freshness). Subscription
   * auto-cleans via takeUntilDestroyed - no manual unsubscribe needed.
   */
  private wireVehicleStream(): void {
    this.realtime.vehicleUpdates$
      .pipe(takeUntilDestroyed(this.destroyRef))
      .subscribe((vehicleUpdate: Vehicle) => {
        // Skip vehicles missing position - backend emits undefined lat/lng for
        // vehicles that haven't reported a Position message yet.
        if (vehicleUpdate.latitude == null || vehicleUpdate.longitude == null) {
          return;
        }

        // 1 upsert vehicle Marker to map
        upsertMarker(this.map!, this.markers, vehicleUpdate);

        // 2 update vehicles() signal
        this.vehicles.update((current) => {
          const idx = current.findIndex(
            (vehicle) => vehicle.vehicleId === vehicleUpdate.vehicleId,
          );
          if (idx === -1) {
            // insert
            return [...current, vehicleUpdate];
          }
          // update
          const copy = [...current];
          copy[idx] = vehicleUpdate;
          return copy;
        });

        // 3 update latestStreamTs()
        const latestTs = this.latestStreamTs();
        if (
          !latestTs ||
          Date.parse(vehicleUpdate.timestamp) > Date.parse(latestTs)
        ) {
          this.latestStreamTs.set(vehicleUpdate.timestamp);
        }
      });
  }

  /**
   * Forwards each SignalR alert push into the alerts signal, prepended
   * so newest sits at index 0. AlertsPanelComponent trusts that ordering
   * downstream. Subscription auto-cleans via takeUntilDestroyed.
   */
  private wireAlertStream(): void {
    this.realtime.alertsReceived$
      .pipe(takeUntilDestroyed(this.destroyRef)) // Ang 16+ - no need for ngOnDestroy, when the component is destroyed, the subscription is automatically cleaned up
      .subscribe((alert: Alert) => {
        this.alerts.update((current) => [alert, ...current]);
      });
  }

  /**
   * Starts the single 5s interval that (a) bumps freshnessNow so the
   * isStale computed re-evaluates, and (b) prunes stale markers whose
   * lastSeenAt has exceeded VEHICLE_MARKER_TTL_MS. One timer, two jobs,
   * same cadence (SP1-09 decision A).
   */
  private startFreshnessTimer(): void {
    // LEARN: executes setInterval every 5 seconds
    // store the timer handler in freshnessIntervalId for ngOnDestroy to stop the timer
    this.freshnessIntervalId = setInterval(() => {
      // Set the freshnessNow signal to now, so the isStale computed re-evaluates
      this.freshnessNow.set(Date.now());

      if (this.map) {
        // LEARN: Remove any vehicle from map whose lastSeenAt is older than ttlMs.
        pruneStale(this.map, this.markers, VEHICLE_MARKER_TTL_MS, Date.now());
      }
    }, FRESHNESS_RE_EVAL_INTERVAL_MS);
  }

  /**
   * Handler bound to FiltersBarComponent's routeChange output.
   */
  onRouteChange(route: string | null): void {
    this.selectedRoute.set(route);
  }
}
