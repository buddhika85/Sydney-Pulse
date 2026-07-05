// freshness.util.ts - pure helpers behind the live-dashboard stale-data badge.
//
// Two independent inputs so neither channel alone can silently go stale:
//   - feedTs   = last TfNSW GTFS poll completion (from GET /api/vehicles envelope)
//   - streamTs = rolling max Vehicle.timestamp observed on the SignalR stream
//
// Threshold 60s + 5s re-eval cadence locked in SP1-09 decision A and the
// 2026-07-01 SP1-10 Phase 2 prep lock. ADR-0013 rules out a periodic HTTP
// refetch - this badge is the visible signal that the stream itself has
// stopped delivering, so the calculation must stay honest.

/**
 * Return the more-recent ISO timestamp of the two inputs. Null-safe.
 *
 * Acceptance criteria (SP1-10 Phase 3 impl target, ~10 min):
 * - both null           -> null
 * - one null            -> the non-null value
 * - both set            -> whichever parses to the higher Date.parse() value
 * - equal timestamps    -> either (caller doesn't depend on which)
 */
export function computeLatestEventTimestamp(
  feedTs: string | null,
  streamTs: string | null,
): string | null {
  throw new Error('SP1-10 Phase 3 - not implemented');
}

/**
 * True when no fresh event has been observed within thresholdMs.
 * Caller re-invokes every 5s with a fresh `now` (see LiveComponent freshness timer).
 *
 * Acceptance criteria (SP1-10 Phase 3 impl target, ~10 min):
 * - latestTs null                             -> true  (no data yet)
 * - now - Date.parse(latestTs) > thresholdMs  -> true
 * - otherwise                                 -> false
 * - default thresholdMs = 60_000              (Phase 1 lock)
 */
export function isStale(
  latestTs: string | null,
  now: number,
  thresholdMs = 60_000,
): boolean {
  throw new Error('SP1-10 Phase 3 - not implemented');
}
