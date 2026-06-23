// Live dashboard - the main commuter-facing surface. Placeholder for
// SP1-09; SP1-10 wires Leaflet map + RealtimeService streams + alerts
// banner. Standalone + OnPush per web/CLAUDE.md conventions.
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'sp-live',
  standalone: true,
  templateUrl: './live.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LiveComponent {}
