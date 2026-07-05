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

import { ChangeDetectionStrategy, Component, computed, input } from '@angular/core';

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
    throw new Error('SP1-10 Phase 3 - not implemented');
  });

  /**
   * Map an Alert severity string to a Tailwind class for the left border
   * of each alert card. Central point so the design token stays in one
   * place if the palette shifts before Sprint 1 close.
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~5 min):
   * - 'severe' / 'critical' -> a red-tone border class
   * - 'warning'             -> an amber-tone border class
   * - anything else         -> a neutral / info-tone border class
   * - case-insensitive input (TfNSW mixes casing across feeds)
   */
  severityClass(alert: Alert): string {
    throw new Error('SP1-10 Phase 3 - not implemented');
  }
}
