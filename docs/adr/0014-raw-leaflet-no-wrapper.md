# ADR-0014 — Raw Leaflet with Angular lifecycle (no ngx-leaflet wrapper)

**Date:** 2026-06-30
**Status:** Accepted

## Context

SP1-10 builds the live dashboard. The dashboard renders a Leaflet map with
one marker per active TfNSW vehicle (~300 at peak), driven by a Cosmos
snapshot at mount and SignalR `vehicleUpdated` messages thereafter.

Three options were evaluated for integrating Leaflet into the Angular
component tree:

1. **`@asymmetrik/ngx-leaflet` wrapper** — the community-standard Angular
   binding for Leaflet. Exposes the map, layers, and events as declarative
   `[leafletOptions]` / `[leafletLayers]` Inputs.
2. **Raw `leaflet` + manual lifecycle** — install the `leaflet` package,
   instantiate `L.map(...)` against a `@ViewChild` element reference inside
   `ngAfterViewInit`, tear down in `ngOnDestroy`. No Angular-flavoured
   abstraction.
3. **Build a thin internal wrapper component** — encapsulate Leaflet behind
   a `MapComponent` exposing custom `@Input() markers` / `@Output() click`.

SP1-09 locked Option 2 informally during the Angular-architecture spike
(captured in the `project_sp109_angular_decisions` memory). This ADR
formalizes that decision in the public record so the reasoning is
discoverable.

## Decision

**Option 2 — raw `leaflet` with manual `AfterViewInit` / `OnDestroy`
lifecycle.** No `ngx-leaflet`, no internal `MapComponent` wrapper. The
`LiveComponent` owns a `@ViewChild('mapContainer')` element reference and
holds the `L.Map` instance directly as a private field.

## Reasoning

**Wrapper maintenance state is the same risk vector that produced SP-20.**
`@asymmetrik/ngx-leaflet` trails Angular's major-version cadence and has
inconsistent upkeep. SP-20 was the Sprint 1 tech-debt row for high-severity
Angular advisories in indirect dependencies; the only reason it closed
in-sprint with zero app-code change (commit `dae300a`) was that nothing
sat between us and the framework. Adding a poorly-maintained third-party
wrapper introduces exactly the kind of indirection that turns a clean
`npm audit --omit=dev` into another 8-advisory carry-forward.

**Leaflet is imperative; Angular's lifecycle handles imperative resources
well.** `ngAfterViewInit` and `ngOnDestroy` are the canonical seams for
"create a DOM-attached resource, tear it down cleanly." The wrapper's
contribution is translating that lifecycle into declarative Inputs — but
we run one map instance per page, owned by one component, with no need
to template-bind it. The declarative translation buys nothing.

**Our state is signal-driven; the wrapper would add a parallel stream
system.** `ngx-leaflet` exposes its own `Subject`-based event surface;
we already have Signals + a `BehaviorSubject<Map<vehicleId, Vehicle>>` in
the component. Going via the wrapper means mapping between two stream
systems for every marker click. Direct `L.circleMarker(...).on('click',
fn)` calling `signal.set(...)` is shorter and has one fewer abstraction
layer to reason about.

**Marker volume is the perf-critical path.** Three hundred markers
mutating on a 30-second cadence is the hot path. The fewer change-detection
hops between SignalR payload arrival and `L.Marker.setLatLng(...)`, the
better. A wrapper that reflects Inputs through Angular change detection
just to call into Leaflet's imperative API is the kind of indirection that
shows up as map jank under load, then takes a day to chase.

**No transferable knowledge is lost.** The Leaflet API (`L.map`,
`L.tileLayer`, `L.circleMarker`, `withAutomaticReconnect` patterns for
event handlers) is the lingua franca an interviewer expects. Wrapping it
in Angular Inputs teaches the codebase a private dialect without teaching
the reader anything portable.

## Consequences

- `LiveComponent` declares `@ViewChild('mapContainer', { static: false })
  mapContainer!: ElementRef<HTMLDivElement>`. The `L.Map` instance is held
  as a private field and is not exposed via Inputs.
- Lifecycle ownership is explicit:
  `ngAfterViewInit` creates the map, tile layer, and initial markers;
  `ngOnDestroy` calls `map.remove()` (which detaches all event listeners)
  and clears the prune `setInterval` from ADR-0013.
- `package.json` depends on `leaflet` + `@types/leaflet` only. No
  `@asymmetrik/ngx-leaflet`, `ngx-leaflet-draw`, or wrapper variants.
- `styles.scss` imports `leaflet/dist/leaflet.css` globally. The framework
  CSS is required for the tile-layer pane, attribution control, and
  zoom-control rendering even though we use `circleMarker` rather than
  default pin icons.
- Frontend tests (deferred to SP-21) get a single clean boundary: mock
  `L.map()` or render against a real DOM element. No wrapper to stub.
- If a future surface needs more than one map per page (Sprint 5 ops
  multi-region view is the only plausible candidate), revisit. Single-map
  pages stay raw.
