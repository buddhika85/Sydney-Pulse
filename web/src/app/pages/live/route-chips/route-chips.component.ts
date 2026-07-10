// route-chips.component.ts - colour-coded route filter chips.
//
// Replaces the SP1-10 initial <select> dropdown with a wrap-flow row of
// per-route buttons. Chips carry the route colour as their visual
// signature so the strip doubles as the map legend - filter action and
// legend are the same UI element, one visual signal serves two purposes.
//
// Presentational (web/CLAUDE.md convention): inputs in, outputs out, no
// service injection. Parent LiveComponent owns selectedRoute state; this
// component owns only the button elements and their click events.
//
// "All routes" is represented as null (matches selectedRoute contract set
// by FiltersBarComponent / LiveComponent). A dedicated "All" chip sits
// first in the strip so the reset action is discoverable - clicking an
// active chip a second time to clear was rejected as too hidden.

import {
  ChangeDetectionStrategy,
  Component,
  input,
  output,
} from '@angular/core';

import { pickReadableTextColor } from '../../../shared/color-contrast.util';

/**
 * Shape of one chip's data - route name for the label + hex colour for
 * the border / active fill, plus per-route vehicle + alert counts used
 * to populate the hover tooltip (title attribute) on each chip.
 */
export interface RouteChipOption {
  name: string;
  color: string;
  vehicleCount: number;
  alertCount: number;
}

@Component({
  selector: 'sp-route-chips',
  standalone: true,
  templateUrl: './route-chips.component.html',
  styleUrl: './route-chips.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class RouteChipsComponent {
  // distinct sorted route list, computed upstream in LiveComponent
  readonly routes = input<RouteChipOption[]>([]);

  // null = "All routes" - single absent state, no "" vs null ambiguity
  readonly selectedRoute = input<string | null>(null);

  // emits null when user clicks the "All" chip, the route name otherwise
  readonly routeChange = output<string | null>();

  /**
   * CHANGE SPEC (SP1-10 usability pass - chip hover counts):
   * Two new inputs feed the "All" chip's hover tooltip so users get a
   * per-strip summary ("40 vehicles, 20 alerts") without needing to
   * hover each individual chip. Kept as inputs (Option A from the
   * usability-pass planning) so this component stays presentational -
   * parent LiveComponent already has the raw arrays, summing is cheap
   * there, and the "All" chip does not need a full RouteChipOption
   * shape (no name, no colour semantics).
   *
   * Defaults to 0 so the tooltip degrades gracefully if the parent
   * hasn't wired the bindings yet (e.g. first paint before feed
   * lands). Template consumption is added in route-chips.component.html
   * as the next scaffolding step.
   */
  readonly totalVehicleCount = input<number>(0);
  readonly totalAlertCount = input<number>(0);

  /**
   * Click handler bound to every chip in the strip, including the "All"
   * chip. Forwards the chip's value straight to the parent - this
   * component holds no local selection state, the source of truth is
   * LiveComponent.selectedRoute.
   *
   * Acceptance criteria:
   * - Just emits the value received. No conditional, no normalisation.
   * - "All" chip passes null via the template; route chips pass the name.
   *
   * @param value  null for the "All" chip; the route name for any other.
   */
  onChipClick(value: string | null): void {
    this.routeChange.emit(value);
  }

  /**
   * Template helper - returns the text colour to use on top of a given
   * chip background so the label stays legible on light lines (T1
   * yellow-orange) and dark lines (T8 green). Thin wrapper over the
   * shared util so the template stays declarative.
   *
   * Acceptance criteria:
   * - Delegate to pickReadableTextColor. One-line body.
   * - No caching yet - route list is <20 today, recomputation cost is
   *   negligible. Revisit if the chip strip renders 100+ items in
   *   Sprint 2 bus scope.
   *
   * @param backgroundHex  the chip's background colour, e.g. "#0098CD".
   */
  textFor(backgroundHex: string): '#000000' | '#ffffff' {
    return pickReadableTextColor(backgroundHex);
  }
}
