// filters-bar.component.ts - route filter dropdown for the live dashboard.
//
// Presentational (web/CLAUDE.md convention): inputs in, outputs out, no
// service injection. Parent LiveComponent owns state; this component owns
// only the <select> element and its change event.
//
// SP1-10 Phase 1 lock: options source from distinct routeShortName values
// in the LOADED vehicle stream, not from /api/routes - the static route
// catalogue carries ~200 entries with most empty. Parent computes the
// distinct sorted set and passes it here.
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

@Component({
  selector: 'sp-filters-bar',
  standalone: true,
  templateUrl: './filters-bar.component.html',
  styleUrl: './filters-bar.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class FiltersBarComponent {
  // distinct sorted routeShortName list, computed upstream in LiveComponent
  readonly routes = input<string[]>([]);

  // null = "All routes" - single absent state, no "" vs null ambiguity
  readonly selectedRoute = input<string | null>(null);

  // emits null when user picks "All routes", the route name otherwise
  readonly routeChange = output<string | null>();

  /**
   * <select> change handler. Normalises the DOM value ("" for the
   * All-routes option) to null before emitting so the parent signal
   * holds one absent-state only.
   *
   * Acceptance criteria (SP1-10 Phase 3 impl target, ~5 min):
   * - value === ""  -> routeChange.emit(null)
   * - value !== ""  -> routeChange.emit(value)
   */
  onSelect(selectedValue: string): void {
    if (selectedValue === '') {
      // no filter applied - show all routes
      this.routeChange.emit(null);
    } else {
      // a filter applied
      this.routeChange.emit(selectedValue);
    }
  }
}
