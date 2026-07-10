// vehicle-marker.ts - Leaflet marker upsert + TTL prune for the live map.
//
// Vehicles arrive twice: once as a bulk snapshot from GET /api/vehicles,
// then continuously as SignalR vehicleUpdated pushes. Both paths call
// upsertMarker() so there is exactly one code path that owns marker
// lifecycle - moving an existing marker vs creating a new one.
//
// TTL prune reflects ADR-0013 (trust the stream, no periodic refetch):
// the client's Cosmos-aligned 5 min TTL matches the vehicles container
// TTL, so a vehicle absent from the stream for that long is safe to drop
// from the map without a re-hydrate call. LiveComponent invokes prune on
// the same 5s cadence as the freshness re-eval.
//
// L.circleMarker (not L.marker) chosen at Phase 1 - route colour is the
// dominant visual signal and default marker icons hide it under a pin.

import * as L from 'leaflet';

import { Vehicle } from '../../models';
import {
  MARKER_FILL_OPACITY,
  MARKER_RADIUS_PX,
  MARKER_STROKE_WEIGHT_PX,
} from '../../shared/design-tokens';

/**
 * Cache entry: the Leaflet marker + when we last saw an update for it.
 * lastSeenAt is a wall-clock ms (Date.now()) so prune math stays a
 * plain subtraction with no timezone or parse cost.
 */
export interface MarkerEntry {
  marker: L.CircleMarker;
  lastSeenAt: number;
}

/**
 * Add or move the marker for one vehicle. Idempotent per vehicleId:
 * a second call with the same id must reuse the existing marker,
 * never leak a second one on top.
 *
 * CHANGE SPEC (SP1-10 marker visibility pass):
 * Add two options to the CircleMarker created on cache miss:
 *   fillColor: routeColor,
 *   fillOpacity: MARKER_FILL_OPACITY,
 * ...and on cache hit, extend the existing setStyle call to include
 * both new keys as well (so a mid-stream route/color drift updates the
 * fill too, not just the border). Import MARKER_FILL_OPACITY from the
 * design-tokens module alongside the existing two constants.
 *
 * - returns the marker (new or existing) so the caller can chain
 *   listeners in later sprints (e.g. click-to-details in SP4)
 */
export function upsertMarker(
  map: L.Map,
  cache: Map<string, MarkerEntry>, // vehicle.vehicleId key, MarkerEntry value
  vehicle: Vehicle,
  now: number = Date.now(),
): L.CircleMarker {
  const tooltip = `${vehicle.routeShortName} · ${vehicle.vehicleId}`;
  const routeColor = vehicle.routeColor;
  let vehicleCache: MarkerEntry | undefined = cache.get(vehicle.vehicleId);

  if (vehicleCache === undefined) {
    // cache miss - insert
    vehicleCache = {
      marker: new L.CircleMarker([vehicle.latitude, vehicle.longitude], {
        radius: MARKER_RADIUS_PX,
        color: routeColor,
        weight: MARKER_STROKE_WEIGHT_PX,
        fillColor: routeColor,
        fillOpacity: MARKER_FILL_OPACITY,
      }),
      lastSeenAt: now,
    };

    vehicleCache.marker.bindTooltip(tooltip);
    vehicleCache.marker.addTo(map);
    cache.set(vehicle.vehicleId, vehicleCache);

    return vehicleCache.marker;
  }
  // cache hit - update
  vehicleCache.marker.setLatLng([vehicle.latitude, vehicle.longitude]);
  vehicleCache.lastSeenAt = now;

  // TfNSW changed route unexpectedly
  vehicleCache.marker.bindTooltip(tooltip);
  vehicleCache.marker.setStyle({ color: routeColor, fillColor: routeColor });

  return vehicleCache.marker;
}

/**
 * Remove any marker whose lastSeenAt is older than ttlMs. Called on a
 * 5s cadence from LiveComponent alongside the freshness re-eval.
 *
 * Acceptance criteria (SP1-10 Phase 3 impl target, ~15 min):
 * - iterate cache entries
 * - if now - entry.lastSeenAt > ttlMs: map.removeLayer(entry.marker),
 *   cache.delete(id)
 * - return the number of entries removed (useful for a debug log +
 *   Sprint 5 anomaly signals; caller may ignore)
 * - default ttlMs = 5 * 60_000 at the call site (ADR-0013), NOT here
 *   - keep this util pure of policy constants
 */
export function pruneStale(
  map: L.Map,
  cache: Map<string, MarkerEntry>,
  ttlMs: number,
  now: number = Date.now(),
): number {
  let prunedEntries = 0;
  cache.forEach((entry, key) => {
    if (now - entry.lastSeenAt > ttlMs) {
      map.removeLayer(entry.marker);
      cache.delete(key);
      ++prunedEntries;
    }
  });
  return prunedEntries;
}
