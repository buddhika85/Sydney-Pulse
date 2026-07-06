// design-tokens.ts - shared visual + timing tokens for the app.
//
// Purpose: eliminate scattered magic numbers so a single edit changes
// them everywhere. This file grows one sprint at a time - today it
// carries SP1-10 dashboard tokens only.
//
// Deliberately NOT included yet (single-consumer today, extract on
// second use to avoid premature abstraction):
//   - freshness pill hex colours (live.component.scss)
//   - alert severity -> Tailwind class map (alerts-panel.component.ts)
//   - alerts rail width 22rem (live.component.scss)
//
// Convention: unit lives in the identifier suffix (_MS, _PX, _LAT, _LNG)
// so a reader doesn't have to trace to the type.

// ---- vehicle marker geometry (vehicle-marker.ts) ----
export const MARKER_RADIUS_PX = 6;
export const MARKER_STROKE_WEIGHT_PX = 1;

// ---- freshness + prune timing (live.component.ts) ----

// Re-eval cadence for both the freshness computed and pruneStale; one
// interval, two jobs on the same tick (SP1-09 decision A).
export const FRESHNESS_RE_EVAL_INTERVAL_MS = 5_000;

// Binary stale threshold (Phase 1 lock). Mirrored as freshness.util
// isStale()'s default arg - keep them equal on any change.
export const FRESHNESS_THRESHOLD_MS = 60_000;

// ADR-0013 - matches the Cosmos vehicles container TTL so a vehicle
// absent from the stream for that long is safe to drop from the map
// without a re-hydrate call.
export const VEHICLE_MARKER_TTL_MS = 5 * 60_000;

// ---- map default view (live.component.ts) ----

// Sydney CBD, zoom 12 covers the metro train network at a glance.
export const SYDNEY_CBD_LAT = -33.8688;
export const SYDNEY_CBD_LNG = 151.2093;
export const DEFAULT_MAP_ZOOM = 12;
