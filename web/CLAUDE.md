# Web (Angular 18+)

Context for Claude Code when working in `/web/`. The root `CLAUDE.md`
covers project-wide rules; this file covers Angular-specific patterns.

## Project layout

```
src/
  app/
    pages/              One folder per route, each a standalone component
      landing/
      live/
      analytics/
      ops/
    services/           Singleton services (SignalR, HTTP, route metadata)
    shared/             Shared components (header, footer, badges)
    models/             TypeScript interfaces matching /docs/api.md
  styles.scss           Tailwind imports + global rules
  environments/         environment.ts, environment.prod.ts
  app.config.ts         Standalone app configuration
  app.routes.ts         Route table (lazy-loaded components)
  main.ts               Bootstrap
```

## Angular conventions

- **Standalone components only.** No NgModules.
- **Routes are lazy-loaded.** Each page is loaded on-demand:
  `loadComponent: () => import('./pages/live/live.component').then(m => m.LiveComponent)`
- **Signals over BehaviorSubjects for component-local state.**
  Use signals for things like UI filters; use Observables for streams.
- **OnPush change detection** on every component:
  `@Component({ changeDetection: ChangeDetectionStrategy.OnPush })`
- **Pure functions in templates are fine** if they're cheap. For
  anything expensive, use a `computed()` signal.

## SignalR integration

SignalR connections are exposed as Observables via a singleton
`RealtimeService`. Two hubs (`vehicles`, `alerts`) per the SP1-09 decision B
topology mirror. Hub + event name constants live in
`services/signalr-events.constants.ts` (mirrors backend `FunctionConstants.cs`
— drift here = silent dropped messages, Debug Story #20).
Components subscribe to whichever stream they need:

```typescript
@Injectable({ providedIn: 'root' })
export class RealtimeService {
  private readonly vehicleUpdates = new Subject<Vehicle>();
  private readonly alertsReceived = new Subject<Alert>();
  private vehiclesHub: HubConnection | null = null;
  private alertsHub: HubConnection | null = null;

  readonly vehicleUpdates$ = this.vehicleUpdates.asObservable();
  readonly alertsReceived$ = this.alertsReceived.asObservable();

  // Idempotent. Builds both hubs, wires .on() before .start(), publishes
  // refs only after both starts succeed (no half-connected state).
  async connect(): Promise<void> { /* ... */ }

  // Idempotent. allSettled so a stop() failure doesn't strand refs.
  async disconnect(): Promise<void> { /* ... */ }
}
```

Components subscribe with `async` pipe or via `toSignal()`:

```typescript
// In a component:
private readonly realtime = inject(RealtimeService);
vehicleUpdates = toSignal(this.realtime.vehicleUpdates$, { initialValue: null });
```

## State management

- **No NgRx.** RxJS + services + signals is enough.
- **Singleton services** hold the canonical state. Components read from
  them, never duplicate.
- **HTTP state goes in services**, not components. Page components are
  presentational; they get data via service injection.

## Styling

- **Tailwind CSS** for layout and spacing
- **CSS variables** for theme colors that match the design system
- **No Angular Material** (ADR-0005). Custom components match the
  Tailwind design tokens.
- **One global stylesheet** (`styles.scss`) for Tailwind imports and
  resets. Component-level `.scss` only for component-specific overrides.

## Component patterns

- **Page components** are thin — they coordinate services and pass
  data into presentational components.
- **Presentational components** take inputs, emit outputs, no service
  injection.
- **Container components** (rare) coordinate multiple presentational
  components with their own state.

## Testing

Frontend unit tests are **deferred from Sprint 1** to backlog item
[SP-21](https://gsoft85512.atlassian.net/browse/SP-21). Sprint 1 ships
the live URL without frontend test coverage; the Angular CLI auto-scaffolded
`app.component.spec.ts` stays so `ng test` infrastructure is intact for
SP-21 to build on.

When SP-21 is picked up the scope is:

- **Unit tests** for services using `TestBed` and HTTP testing utilities
  (`RealtimeService`, `VehiclesService`, `AlertsService`, `RoutesService`)
- **Component tests** with `ComponentFixture`, but limit scope — heavy
  template testing has diminishing returns
- **E2E tests** with Playwright in `/e2e/` (Sprint 4 enhancement;
  skipped initially)

## Common tasks

- Add a new route: create folder under `pages/`, scaffold standalone
  component, add lazy route in `app.routes.ts`
- Add a new SignalR event: add hub + event names to
  `services/signalr-events.constants.ts` (mirror backend
  `FunctionConstants.cs`), extend `RealtimeService` with a new
  `Subject<T>` + `.asObservable()` pair, update `/docs/api.md`
- Add a new API call: extend the relevant service in `services/`,
  type the response matching `/docs/api.md`

## Don't

- Don't use NgModules (Angular 18+ doesn't need them).
- Don't inject services into presentational components — they should
  receive data via inputs only.
- Don't use `subscribe` in components if `async` pipe or `toSignal`
  works. Manual subscription invites leaks.
- Don't use `any`. Strict mode means types are enforced; lean into it.
- Don't use Angular Material despite the temptation. Tailwind components
  match the dashboard mockups exactly; Material would diverge.
- Don't write inline HTML templates longer than ~30 lines. Use a
  separate `.html` file.
