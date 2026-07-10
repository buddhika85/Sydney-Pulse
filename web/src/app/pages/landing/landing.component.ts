// landing.component.ts - public marketing surface at "/".
// Portfolio-first framing: recruiter lands here, clicks through to the
// live dashboard within 3 seconds, leaves impressed by the tech signal.
// Standalone + OnPush per web/CLAUDE.md; RouterLink for the primary
// CTA (SPA nav, no full reload) and plain <a> for external links.
//
// No service injection, no state - the page is pure static content.
// URLs are readonly consts on the class so the HTML stays declarative
// and changes stay in one file.
import { ChangeDetectionStrategy, Component } from '@angular/core';
import { RouterLink } from '@angular/router';

@Component({
  selector: 'sp-landing',
  standalone: true,
  imports: [RouterLink],
  templateUrl: './landing.component.html',
  styleUrl: './landing.component.scss',
  changeDetection: ChangeDetectionStrategy.OnPush,
})
export class LandingComponent {
  // Primary CTA target - the Live dashboard route inside this SPA.
  readonly dashboardRoute = '/live';

  // External profile + repo links. Hardcoded rather than env-driven
  // because these do not change between dev and prod deploys.
  readonly repoUrl = 'https://github.com/buddhika85/Sydney-Pulse';
  readonly linkedInUrl = 'https://www.linkedin.com/in/buddhikagunasekera/';

  // Sprint-lock version badge. Bumped by hand at each sprint tag
  // (v0.1.0 = Sprint 1); no automation needed at this scale.
  readonly version = 'v0.1.0';

  // Tech stack badges - listed once here so template stays declarative
  // and reordering / adding a badge is one edit. Order chosen to lead
  // with backend .NET/Azure signal (target roles are senior .NET+Azure).
  readonly techStack: readonly string[] = [
    '.NET 8',
    'Azure Functions',
    'Managed Identity',
    'Event Grid',
    'Service Bus',
    'Cosmos DB',
    'SignalR',
    'Application Insights',
    'Bicep',
    'GitHub Actions',
    'xUnit',
    'Angular 20',
    'Leaflet',
    'Tailwind',
  ];
}
