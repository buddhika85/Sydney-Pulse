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
  effect,
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
import {
  MarkerEntry,
  pruneStale,
  pulseMarker,
  upsertMarker,
} from './vehicle-marker';
import {
  DEFAULT_MAP_ZOOM,
  FRESHNESS_RE_EVAL_INTERVAL_MS,
  MARKER_RADIUS_PX,
  SYDNEY_CBD_LAT,
  SYDNEY_CBD_LNG,
  VEHICLE_MARKER_TTL_MS,
  MARKER_PULSE_PEAK_SCALE,
  MARKER_PULSE_DURATION_MS,
} from '../../shared/design-tokens';
import { firstValueFrom } from 'rxjs';
import { environment } from '../../../environments/environment';

import { RouteChipOption } from './route-chips/route-chips.component';

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

  /**
   * Wires the route-filter effect. Re-applies map marker visibility
   * whenever `selectedRoute` changes (user clicks a chip) OR `vehicles`
   * changes (SignalR push adds/updates a vehicle). One place keeps both
   * filter-toggle and new-arrival paths in sync.
   *
   */
  constructor() {
    effect(() => {
      // read both signals to establish dependencies for re-runs
      this.selectedRoute();
      this.vehicles();
      this.applyRouteFilterToMap();
    });
  }

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

  /**
   * Distinct route list for the filter chip strip - one entry per
   * routeShortName seen in the current vehicles snapshot. Sourced from
   * the vehicle feed, NOT /api/routes (Phase 1 lock: the static catalogue
   * carries ~200 entries with most empty).
   *
   * CHANGE SPEC (SP1-10 chip refactor):
   * - Previous shape: `computed<string[]>` returning just the sorted
   *   names. Chip UI needs colour too, so the shape changes to
   *   `computed<RouteChipOption[]>` (`{ name, color }` per entry).
   * - Import: add `RouteChipOption` from `./route-chips/route-chips.component`
   *   at the top of this file alongside the existing component imports.
   * - Dedup rule: use a `Map<name, color>` and set-if-absent - first-seen
   *   colour wins. Matches the invariant "one route -> one colour"
   *   guaranteed by the backend feed. If TfNSW ever ships colour drift
   *   mid-session the first observation is chosen deterministically.
   * - Sort rule: alphabetically by name via `String.localeCompare` so
   *   `"T10"` sorts naturally next to `"T9"` if that ever appears
   *   (default `<` comparator sorts "T10" before "T2").
   *
   * - Downstream consumers: FiltersBarComponent input type flips from
   *   `string[]` to `RouteChipOption[]` in step 4. The template binding
   *   `[routes]="routeOptions()"` in live.component.html stays unchanged.
   *
   * CHANGE SPEC (SP1-10 usability pass - chip hover counts):
   * - `RouteChipOption` gained `vehicleCount` + `alertCount` fields so
   *   each chip can render a hover tooltip. This computed now needs to
   *   populate both per route.
   * - Vehicle count: tally per `routeShortName` in the same iteration
   *   that builds the colour map - one loop over `this.vehicles()`.
   * - Alert count: separate loop over `this.alerts()`. Alerts and
   *   vehicles are independent arrays with the same partition key.
   *   Routes with alerts but no active vehicles remain out of scope
   *   (known deferral - see stories memory file); they simply won't
   *   appear as chips this sprint.
   * - Sort order unchanged: alphabetical by name via `localeCompare`.
   * - Return shape wired below as `{ name, color, vehicleCount, alertCount }`.
   *   Placeholders (0) are in place so the build stays green while the
   *   counting logic is written - replace before shipping.
   */
  readonly routeOptions = computed<RouteChipOption[]>(() => {
    // LEARN: iterate vehicles → first-seen routeShortName wins its colour
    // → convert entries to { name, color }[] → sort by name via localeCompare.
    // one route -> one colour
    const routeInfoByShortNameMap = new Map<
      string,
      { color: string; vehicleCount: number; alertCount: number }
    >();

    // assign colour and count vehicles on each route
    this.vehicles().forEach((vehicle) => {
      const existingRouteInfo = routeInfoByShortNameMap.get(
        vehicle.routeShortName,
      );
      if (!existingRouteInfo) {
        routeInfoByShortNameMap.set(vehicle.routeShortName, {
          color: vehicle.routeColor,
          vehicleCount: 1,
          alertCount: 0,
        });
      } else {
        routeInfoByShortNameMap.set(vehicle.routeShortName, {
          ...existingRouteInfo,
          vehicleCount: existingRouteInfo.vehicleCount + 1,
        });
      }
    });

    // count alerts per route
    this.alerts().forEach((alert) => {
      const existingRouteInfo = routeInfoByShortNameMap.get(
        alert.routeShortName,
      );
      if (existingRouteInfo)
        routeInfoByShortNameMap.set(alert.routeShortName, {
          ...existingRouteInfo,
          alertCount: existingRouteInfo.alertCount + 1,
        });
    });

    return [...routeInfoByShortNameMap.entries()]
      .map(([name, { color, vehicleCount, alertCount }]) => ({
        name,
        color,
        vehicleCount,
        alertCount,
      }))
      .sort((a, b) => a.name.localeCompare(b.name));
  });

  /**
   * Filter-aware label for the mode chip in the header. Answers "how
   * many trains are on screen right now?" at a glance, adjusting the
   * phrasing based on whether a route filter is active.
   * - No pluralisation guard - `"1 of 1 vehicles"` is acceptable at
   *   this sprint (Design Call 1 in the SP1-10 usability pass plan)
   * - Read both `selectedRoute()` and `vehicles()` inside the computed
   *   so change detection re-fires on either
   */
  readonly vehicleCountLabel = computed<string>(() => {
    const selectedRoute = this.selectedRoute();
    const vehicles = this.vehicles();
    if (!selectedRoute) {
      // no selection - total count
      return `${vehicles.length} vehicles`;
    }

    // selection exists
    const routeVehicleCount = vehicles.filter(
      (vehicle) => vehicle.routeShortName === selectedRoute,
    ).length;
    return `${routeVehicleCount} of ${vehicles.length} vehicles`;
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

    if (environment.debugging.enableSignalRRealtime) {
      await this.startRealtime();
    }
    if (environment.debugging.enableFreshnessTimer) {
      this.startFreshnessTimer();
    }
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
        const marker = upsertMarker(this.map!, this.markers, vehicleUpdate);
        // animate marker movement
        if (environment.features.pulseMarkers) {
          pulseMarker(
            marker,
            MARKER_RADIUS_PX,
            MARKER_PULSE_PEAK_SCALE,
            MARKER_PULSE_DURATION_MS,
          );
        }

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
        this.alerts.update((currentAlertList) => {
          // different alerts can have same alert Id but with different route short names
          // means - a TfNSW multi route alert
          const alertIndex = currentAlertList.findIndex(
            (a) =>
              a.alertId === alert.alertId &&
              a.routeShortName === alert.routeShortName,
          );
          if (alertIndex === -1) {
            // never seen this alert before - new alert
            // insert - new alert to top (newest-first)
            return [alert, ...currentAlertList];
          }

          // Existing (alertId, routeShortName) - ADR-0010 dedup contract:
          // Also handles SignalR reconnection replay per ADR-0010.
          const existingVersionOfAlert = currentAlertList[alertIndex];
          // keep the alert with the higher receivedAt (latest broadcast wins).
          if (
            Date.parse(alert.receivedAt) <=
            Date.parse(existingVersionOfAlert.receivedAt)
          ) {
            // incoming alert already exists or older - so, ignoring/discarding incoming alert
            return currentAlertList;
          }

          // incoming alert is newer - upsert needed - TfNSW updated an existing alert
          // LEARN: in-place mutation is not detected by Angular Signals, they need ref change, So, get a copy (force a ref change) and update the copy so that Angular Signal can detect the change
          const copy = [...currentAlertList];
          copy[alertIndex] = alert; // replace in place
          return copy;
        });
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
   * Iterate the marker cache and toggle each marker's map membership
   * based on the current `selectedRoute`. Called from the constructor
   * effect on any change to `selectedRoute` or `vehicles`.
   *
   * Design note: separates visibility (this method) from cache lifecycle
   * (upsertMarker / pruneStale). Filter changes never delete cache
   * entries - only add/remove from the map layer - so switching filters
   * back and forth doesn't re-hydrate markers, only re-shows them.
   *
   * Acceptance criteria:
   * - Read `this.selectedRoute()` once into a local `selected`.
   * - Guard: if `this.map` is null (effect fires before ngAfterViewInit
   *   completes the map init), return early.
   * - Build a lookup Map<vehicleId, routeShortName> by iterating
   *   `this.vehicles()` once. Cheap - <100 entries at Sprint 1 scope.
   * - Iterate `this.markers`. For each `[vehicleId, entry]`:
   *     * Look up the route name via the lookup Map. Skip the entry if
   *       missing (defensive - marker without a matching vehicle should
   *       not happen but silently ignoring keeps the loop robust).
   *     * shouldShow = `selected === null || route === selected`.
   *     * If shouldShow AND `!map.hasLayer(entry.marker)` -> `addTo(map)`.
   *     * Else if !shouldShow AND `map.hasLayer(entry.marker)` -> `removeLayer(entry.marker)`.
   * - Must be idempotent: calling twice with the same state is a no-op
   *   because of the hasLayer guards.
   * - Do NOT delete entries from `this.markers` - visibility only.
   *   TTL prune (via startFreshnessTimer) is the sole cache-eviction path.
   */
  private applyRouteFilterToMap(): void {
    const selectedRoute = this.selectedRoute();
    if (!this.map) return;
    const map = this.map; // const capture narrows into closures
    const vehicleOnRouteMap = new Map<string, string>(); // vehicleId => routeShortName
    this.vehicles().forEach((vehicle) =>
      vehicleOnRouteMap.set(vehicle.vehicleId, vehicle.routeShortName),
    );
    this.markers.forEach((entry: MarkerEntry, vehicleId: string) => {
      const routeOfVehicle = vehicleOnRouteMap.get(vehicleId);
      if (routeOfVehicle) {
        const shouldShow =
          selectedRoute === null || routeOfVehicle === selectedRoute;
        if (shouldShow && !map.hasLayer(entry.marker)) {
          entry.marker.addTo(map);
        } else if (!shouldShow && map.hasLayer(entry.marker)) {
          this.map!.removeLayer(entry.marker);
        }
      }
    });
  }

  /**
   * Handler bound to FiltersBarComponent's routeChange output.
   */
  onRouteChange(route: string | null): void {
    this.selectedRoute.set(route);
  }
}
