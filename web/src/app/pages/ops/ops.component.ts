// Ops page - SRE-facing operational surface (per ADR-0007 SRE is a
// first-class actor). Placeholder for SP1-09; Sprint 4 fills with App
// Insights metrics, deploy slot state, queue depth. Standalone + OnPush.
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'sp-ops',
  standalone: true,
  templateUrl: './ops.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class OpsComponent {}
