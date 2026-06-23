// Analytics page - aggregate views from the Parquet archive. Placeholder
// for SP1-09; Sprint 3 fills this in once SP-19 archive smoke passes and
// KQL/Synapse queries are wired. Standalone + OnPush per web/CLAUDE.md.
import { ChangeDetectionStrategy, Component } from '@angular/core';

@Component({
  selector: 'sp-analytics',
  standalone: true,
  templateUrl: './analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyticsComponent {}
