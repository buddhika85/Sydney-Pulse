// alerts-panel.component.ts - right-rail alerts list on the live dashboard.
//
// Presentational (web/CLAUDE.md convention): inputs in, no service injection.
// Parent LiveComponent owns the merged alert list (initial fetch + SignalR
// alertReceived stream); this component only slices by selectedRoute and
// renders.
//
// Filter model: null selectedRoute -> show every alert; a route name ->
// only alerts whose routeShortName matches. Matches the same null-as-
// absent contract used by FiltersBarComponent so both sides of the header
// stay consistent.
//
// severityClass() intentionally lives on the component (not in a shared
// helper file) - only this template consumes it, and moving it now would
// be premature abstraction per the project no-abstraction-until-needed rule.

import {
  ChangeDetectionStrategy,
  Component,
  computed,
  input,
} from '@angular/core';

import { Alert } from '../../../models';

@Component({
  selector: 'sp-alerts-panel',
  standalone: true,
  templateUrl: './alerts-panel.component.html',
  styleUrl: './alerts-panel.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AlertsPanelComponent {
  // full alert list from parent; already prepended-on-arrival so index 0 is newest
  readonly alerts = input<Alert[]>([]);

  // shared selectedRoute signal from parent; null = no route filter
  readonly selectedRoute = input<string | null>(null);

  /**
   * Route-scoped view of the alert list. Pure derivation - no side effects,
   * safe to call from the template on every change-detection cycle.
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~10 min):
   * - selectedRoute() null       -> return alerts() unchanged
   * - selectedRoute() set        -> return alerts filtered by
   *                                 a.routeShortName === selectedRoute()
   * - preserves input ordering (newest-first, parent guarantees this)
   */
  readonly visibleAlerts = computed<Alert[]>(() => {
    const alerts = this.alerts();
    const selectedRoute = this.selectedRoute();

    // no selection, so, no alert filtering
    if (!selectedRoute) {
      return alerts;
    }

    // filter by route selected
    return alerts.filter((alert) => alert.routeShortName === selectedRoute);
  });

  /**
   * Filter-aware header label. Answers "how many alerts are showing
   * right now, and how does that compare to the total?" for the
   * ACTIVE ALERTS header of this panel.
   *
   * Acceptance criteria:
   * - When `selectedRoute()` is null: return `"Active alerts (N)"`
   *   where N = `alerts().length` (e.g. `"Active alerts (20)"`)
   * - When `selectedRoute()` is set: return `"Active alerts (X of Y)"`
   *   where X = `visibleAlerts().length` and Y = `alerts().length`
   *   (e.g. `"Active alerts (3 of 20)"`)
   * - No pluralisation guard - `"Active alerts (1 of 1)"` is acceptable
   *   at this sprint (matches Design Call 1 in the SP1-10 usability
   *   pass plan; same call as vehicleCountLabel on LiveComponent)
   * - Read `alerts()`, `visibleAlerts()`, and `selectedRoute()` so
   *   change detection re-fires on any of them
   */
  readonly headerLabel = computed<string>(() => {
    const totalAlertCount = this.alerts().length;
    if (!this.selectedRoute()) {
      // no selected route
      return `Active alerts (${totalAlertCount})`;
    }

    // a route selected
    return `Active alerts (${this.visibleAlerts().length} of ${totalAlertCount})`;
  });

  /**
   * Maps GTFS-Realtime Alert.Effect values (lowercased, no underscores)
   * to a Tailwind border class. See docs/sp1-10-debug-stories.md #1
   * for the taxonomy discovery.
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~5 min):
   * - noservice / significantdelays / reducedservice / stopmoved -> red border
   * - modifiedservice / detour                                    -> amber border
   * - additionalservice / unknowneffect / othereffect / noeffect  -> gray (default)
   * - anything unknown -> gray (default)
   * - case-insensitive input (backend already lowercases; belt+braces here)
   */
  severityClass(alert: Alert): string {
    switch (alert.severity?.toLowerCase()) {
      case 'noservice':
      case 'significantdelays':
      case 'reducedservice':
      case 'stopmoved':
        return 'border-red-500';
      case 'modifiedservice':
      case 'detour':
        return 'border-amber-500';
      default:
        // additionalservice, unknowneffect, othereffect, noeffect, anything new
        return 'border-gray-300';
    }
  }
}
