// Analytics page - aggregate views from the Parquet archive. Placeholder
// for SP1-09; Sprint 3 fills this in once SP-19 archive smoke passes and
// KQL/Synapse queries are wired. Standalone + OnPush per web/CLAUDE.md.
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'sp-analytics',
  standalone: true,
  // RouterLink added SP1-13 so the placeholder can cross-link to the
  // evidence page + live dashboard as internal SPA navigation.
  imports: [RouterLink],
  templateUrl: './analytics.component.html',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class AnalyticsComponent {}
