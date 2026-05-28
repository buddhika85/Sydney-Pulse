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

The SignalR connection is exposed as Observables via a singleton
`SignalRService`. Components subscribe to whichever stream they need:

```typescript
@Injectable({ providedIn: 'root' })
export class SignalRService {
  private connection: HubConnection;
  readonly vehicles$: Observable<VehicleUpdate>;
  readonly alerts$: Observable<AlertPublished>;

  constructor(private http: HttpClient) {
    this.vehicles$ = this.createStream('VehicleUpdated');
    this.alerts$ = this.createStream('AlertPublished');
  }

  private createStream<T>(eventName: string): Observable<T> {
    return new Observable<T>(subscriber => {
      this.connection.on(eventName, (data: T) => subscriber.next(data));
      return () => this.connection.off(eventName);
    }).pipe(share());
  }
}
```

Components subscribe with `async` pipe or via `toSignal()`:

```typescript
vehicles = toSignal(this.signalR.vehicles$, { initialValue: [] });
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

- **Unit tests** for services using `TestBed` and HTTP testing utilities
- **Component tests** with `ComponentFixture`, but limit scope — heavy
  template testing has diminishing returns
- **E2E tests** with Playwright in `/e2e/` (Sprint 4 enhancement;
  skipped initially)

## Common tasks

- Add a new route: create folder under `pages/`, scaffold standalone
  component, add lazy route in `app.routes.ts`
- Add a new SignalR event: extend `SignalRService` with a new
  `Observable<T>`, update `/docs/api.md`
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
