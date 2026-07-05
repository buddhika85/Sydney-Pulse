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

import { Alert, Vehicle } from '../../models';
import { AlertsService } from '../../services/alerts.service';
import { RealtimeService } from '../../services/realtime.service';
import { VehiclesService } from '../../services/vehicles.service';
import { AlertsPanelComponent } from './alerts-panel/alerts-panel.component';
import { FiltersBarComponent } from './filters-bar/filters-bar.component';
import { computeLatestEventTimestamp, isStale } from './freshness.util';
import { MarkerEntry, pruneStale, upsertMarker } from './vehicle-marker';

// Sydney CBD, zoom 12 covers the metropolitan train network at a glance.
const SYDNEY_CBD_LAT: number = -33.8688;
const SYDNEY_CBD_LNG: number = 151.2093;
const DEFAULT_ZOOM: number = 12;

// ADR-0013: 5 min matches the Cosmos vehicles container TTL, so a vehicle
// absent from the stream for that long is safe to drop from the map with
// no re-hydrate call. Re-eval every 5s per SP1-09 decision A.
const VEHICLE_MARKER_TTL_MS: number = 5 * 60_000;
const FRESHNESS_RE_EVAL_INTERVAL_MS: number = 5_000;

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
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~15 min):
   * - mapEl() defined (viewChild resolved) - guard, else throw
   * - initMap(mapEl().nativeElement)
   * - await loadInitialVehicles()
   * - await loadInitialAlerts()
   * - await startRealtime()
   * - startFreshnessTimer()
   */
  async ngAfterViewInit(): Promise<void> {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * Cleanup. All three handles need explicit teardown - the map holds
   * DOM + tile requests, the interval keeps a reference alive, and
   * RealtimeService.disconnect() releases the two SignalR Free SKU
   * connection slots (cap 20).
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~10 min):
   * - clearInterval(freshnessIntervalId) if defined
   * - map?.remove(); map = null
   * - markers.clear()
   * - void realtime.disconnect() (fire-and-forget; disconnect is idempotent)
   */
  ngOnDestroy(): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~20 min.
   *
   * Acceptance criteria:
   * - create L.map(el, { center: [SYDNEY_CBD_LAT, SYDNEY_CBD_LNG],
   *   zoom: DEFAULT_ZOOM })
   * - add L.tileLayer OSM (https://tile.openstreetmap.org/{z}/{x}/{y}.png)
   *   with an attribution string
   * - store into this.map
   * - CSS for the tiles is already loaded globally via styles.scss
   *   (@import "leaflet/dist/leaflet.css") - do NOT re-import here
   */
  private initMap(el: HTMLElement): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~15 min.
   *
   * Acceptance criteria:
   * - firstValueFrom(vehiclesService.getVehicles())
   * - set feedTimestamp(response.feedTimestamp)
   * - set vehicles(response.vehicles)
   * - upsertMarker for each vehicle so the initial snapshot is on the map
   *   before the stream lands (ADR-0013 initial-snapshot-then-stream)
   */
  private async loadInitialVehicles(): Promise<void> {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~10 min.
   *
   * Acceptance criteria:
   * - firstValueFrom(alertsService.getAlerts())
   * - set alerts(response) - envelope is already dropped at service boundary
   */
  private async loadInitialAlerts(): Promise<void> {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~10 min.
   *
   * Acceptance criteria:
   * - await realtime.connect() (idempotent; safe if already connected)
   * - wireVehicleStream()
   * - wireAlertStream()
   * - order matters: connect BEFORE wiring so a failure short-circuits
   *   the subscriptions and we don't strand handlers pointing at a
   *   half-open service
   */
  private async startRealtime(): Promise<void> {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~15 min.
   *
   * Acceptance criteria:
   * - subscribe to realtime.vehicleUpdates$ via takeUntilDestroyed(destroyRef)
   * - on each Vehicle:
   *     upsertMarker(this.map!, this.markers, vehicle)
   *     update vehicles() signal (upsert by vehicleId - replace or append)
   *     update latestStreamTs() if vehicle.timestamp > current latestStreamTs
   * - map non-null is a precondition (wireVehicleStream runs after initMap)
   */
  private wireVehicleStream(): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~10 min.
   *
   * Acceptance criteria:
   * - subscribe to realtime.alertsReceived$ via takeUntilDestroyed(destroyRef)
   * - on each Alert: prepend into alerts() signal so newest is index 0
   *   (AlertsPanelComponent preserves that order)
   */
  private wireAlertStream(): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * SP1-10 Phase 3 impl target, ~10 min.
   *
   * Acceptance criteria:
   * - setInterval every FRESHNESS_RE_EVAL_INTERVAL_MS
   * - on tick: freshnessNow.set(Date.now()) - drives the isStale computed
   * - on the same tick: if map is set, pruneStale(map, markers,
   *   VEHICLE_MARKER_TTL_MS) - one interval, two jobs (both on the same
   *   cadence per SP1-09 decision A)
   * - store handle in this.freshnessIntervalId for teardown
   */
  private startFreshnessTimer(): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }

  /**
   * Handler bound to FiltersBarComponent's routeChange output.
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~5 min):
   * - selectedRoute.set(route) - trivial, but kept as a method (not an
   *   inline arrow in the template) so parent template stays readable
   */
  onRouteChange(route: string | null): void {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }
}
