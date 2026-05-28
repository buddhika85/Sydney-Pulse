# ADR-0005: Angular for the frontend

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

The Sydney Pulse frontend must deliver four routes: a landing/case-study
page, a live commuter dashboard with real-time map updates, a reliability
analytics view, and an operations dashboard. Real-time data arrives via
SignalR; state is largely derived from streams.

The project's backend is .NET 8 and the developer's primary frontend
experience is with Angular, not React.

## Decision

Use Angular 18+ with standalone components, RxJS, and Tailwind CSS.

Routing via `@angular/router` with lazy-loaded standalone components per
route. SignalR is consumed through `@microsoft/signalr` and exposed as
`Observable<T>` services. Mapping via Leaflet with `@bluehalo/ngx-leaflet`.
Charts via `ngx-charts`. No Angular Material — custom Tailwind-styled
components match the design language of the dashboards.

## Consequences

Positive:

- The developer ships faster in Angular than learning React mid-project.
  For a four-week portfolio timeline, "use what you know" is the right
  call.
- Strong RxJS + SignalR alignment. The SignalR stream becomes
  `Observable<VehicleUpdate>` that the map, alerts panel, and stats
  counter subscribe to independently. No central store, no Redux/NgRx
  required.
- Strict TypeScript by default in Angular CLI scaffold. Type safety
  across components and services from day one.
- Strong .NET ecosystem alignment — many AZ-400 / DevOps Engineer roles
  in the target market are at Angular shops.
- Standalone components in Angular 18+ removed most historical
  NgModule boilerplate that made Angular feel heavy.

Negative:

- Smaller hiring market for Angular than React in startups. Acceptable
  trade — the target audience for this portfolio is enterprise / cloud
  roles, not startup product roles.
- Larger bundle size than React. Mitigated by code-splitting per route
  and the small overall app size.
- Steeper learning curve for visitors who read the source. Acceptable —
  visitors looking at the *frontend* code are not the primary portfolio
  audience.

## Alternatives considered

**React with Vite.** Rejected because learning React simultaneously with
building a portfolio piece increases risk of missed deadlines. The
developer's React knowledge is shallow.

**Vue.** Rejected — no prior experience and no strong reason to choose
it over Angular.

**Blazor (Server or WebAssembly).** Considered as a .NET-aligned option.
Rejected because Blazor's SignalR integration is opinionated to the
point of being inflexible for this use case, and Blazor talent in the
target hiring market is rare.

## Related decisions

- ADR-0007 — Three primary actors with three dedicated screens; Angular
  handles per-route lazy loading cleanly.
- ADR-0008 — SignalR Free SKU; client connects via `@microsoft/signalr`.

## Portfolio framing

When discussing the frontend choice in interviews, frame as:
*"Stack alignment with .NET and the reactive SignalR data flow makes
Angular + RxJS a natural fit. I built dedicated services exposing
each backend stream as an Observable, so components subscribe
independently without a central store."*
