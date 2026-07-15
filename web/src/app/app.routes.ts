// Route table for the app shell. Each page is lazy-loaded via
// loadComponent so the initial bundle stays small - per web/CLAUDE.md
// "Routes are lazy-loaded". SP1-09 wires placeholders; SP1-10 + SP1-11
// + Sprint 3/4 fill in real content behind these routes.
import { Routes } from '@angular/router';

export const routes: Routes = [
  // Default landing - public marketing surface.
  { path: '', pathMatch: 'full', redirectTo: 'landing' },
  {
    path: 'landing',
    loadComponent: () =>
      import('./pages/landing/landing.component').then((m) => m.LandingComponent),
  },
  {
    path: 'live',
    loadComponent: () =>
      import('./pages/live/live.component').then((m) => m.LiveComponent),
  },
  {
    // Portfolio evidence walkthrough — Loom demo + 8 curated shots
    // covering IaC, CI/CD, tests, Cosmos, App Insights, live map,
    // and Managed Identity RBAC. Added SP1-13 (evidence page extension).
    path: 'evidence',
    loadComponent: () =>
      import('./pages/evidence/evidence.component').then(
        (m) => m.EvidenceComponent,
      ),
  },
  {
    path: 'analytics',
    loadComponent: () =>
      import('./pages/analytics/analytics.component').then(
        (m) => m.AnalyticsComponent,
      ),
  },
  {
    path: 'ops',
    loadComponent: () =>
      import('./pages/ops/ops.component').then((m) => m.OpsComponent),
  },
  // Unknown route - send back to landing rather than a 404 page.
  { path: '**', redirectTo: 'landing' },
];
