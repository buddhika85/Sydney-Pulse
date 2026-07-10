// filters-bar.component.ts - filter bar container for the live dashboard.
//
// Presentational (web/CLAUDE.md convention): inputs in, outputs out, no
// service injection. Composes RouteChipsComponent today; Sprint 2 adds
// mode / agency / service-type widgets here beside the chip strip.
//
// SP1-10 Phase 1 lock: route options source from distinct routeShortName
// values in the LOADED vehicle stream, not from /api/routes - the static
// route catalogue carries ~200 entries with most empty. Parent computes
// the distinct sorted set (with colours) and passes it here.
//
// "All routes" selection is represented as null (not "" or "all"), giving
// selectedRoute a single unambiguous absent state that flows cleanly into
// the alerts-panel filter too.

import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';

import {
  RouteChipOption,
  RouteChipsComponent,
} from '../route-chips/route-chips.component';

@Component({
  selector: 'sp-filters-bar',
  standalone: true,
  templateUrl: './filters-bar.component.html',
  styleUrl: './filters-bar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
  imports: [RouteChipsComponent],
})
export class FiltersBarComponent {
  // distinct sorted route list (name + colour), computed upstream in
  // LiveComponent. Colour rides alongside name so the chip child can
  // style each button without re-scanning the vehicle feed.
  readonly routes = input<RouteChipOption[]>([]);

  // null = "All routes" - single absent state, no "" vs null ambiguity
  readonly selectedRoute = input<string | null>(null);

  // emits null when user picks "All routes", the route name otherwise
  readonly routeChange = output<string | null>();

  /**
   * CHANGE SPEC (SP1-10 usability pass - chip hover counts):
   * Pass-throughs to RouteChipsComponent's aggregate "All" chip tooltip.
   * FiltersBarComponent doesn't consume them - kept dumb so LiveComponent
   * stays single source of truth for count semantics.
   * Defaults to 0 for graceful first-paint before the vehicle feed
   * lands.
   */
  readonly totalVehicleCount = input<number>(0);
  readonly totalAlertCount = input<number>(0);
}
