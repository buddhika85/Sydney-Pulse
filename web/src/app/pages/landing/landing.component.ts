// Landing page - the public marketing surface. Placeholder for SP1-09;
// SP1-11 will flesh out hero + CTA + architecture SVG.
// Standalone + OnPush per web/CLAUDE.md conventions.
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'sp-landing',
  standalone: true,
  templateUrl: './landing.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingComponent {}
