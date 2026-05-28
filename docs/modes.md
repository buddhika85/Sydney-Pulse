# Build and demo modes

Sydney Pulse runs in different modes depending on the environment and what
you're testing. Modes are controlled by configuration, not code changes.

## Mode overview

| Mode | Purpose | Data source | Polling |
|---|---|---|---|
| `live` | Production with real TfNSW data | TfNSW API | On (30s) |
| `demo` | Recorded data replay for interviews | Local JSON fixtures | On (synthesized) |
| `offline` | Local development without TfNSW | Static seed data | Off |

The default mode in production is `live`. The default for `func start`
locally is `offline` to avoid burning the TfNSW daily quota during
development.

## Why this exists

Three concrete reasons:

1. **Demo reliability** — when you walk a recruiter through the live
   dashboard at 2 am Sydney time, almost no vehicles are moving. `demo`
   mode replays a recorded 5-minute slice of peak-hour traffic on a
   loop so the dashboard always looks alive.
2. **Cost control** — running the full poller during local development
   would burn TfNSW API quota (60k/day) and Azure resources for no
   value. `offline` mode short-circuits the network and uses fixtures.
3. **Interview safety** — if TfNSW has an outage during your portfolio
   demo, you can flip prod into `demo` mode via Azure App Configuration
   toggle without redeploying.

## Configuration

Mode is read from the app setting `SydneyPulse__Mode` on Function apps and
from environment variable `NG_MODE` on the Angular app. Bicep parameters
set it per environment; App Configuration overrides at runtime.

```bicep
@allowed(['live', 'demo', 'offline'])
param mode string = 'live'
```

In `appsettings.json` for local development:

```json
{
  "SydneyPulse": {
    "Mode": "offline",
    "OfflineSeedPath": "../SydneyPulse.Tests/Fixtures/seed.json"
  }
}
```

## Per-mode behaviour

### `live` mode

- Poller hits the real TfNSW API every 30 seconds
- All events flow through Event Grid → Service Bus → SignalR as designed
- Application Insights captures real production telemetry
- No fixture data is served anywhere

### `demo` mode

- Poller reads from a recorded fixture file (`fixtures/peak-hour-snapshot.json`)
  every 30 seconds and emits events as if the fixture were live data
- Vehicle positions advance deterministically along recorded paths,
  looping back to the start after 5 minutes
- Alerts are synthesized on a schedule (one new alert every 90 seconds)
- Application Insights still captures telemetry but tagged with
  `customDimensions.mode = "demo"` so it can be filtered out of SLO
  calculations

This mode is useful when:

- Recording a Loom walkthrough at 2 am
- Demo-ing during a live interview when TfNSW might be unreliable
- Testing UI changes against predictable vehicle movement

### `offline` mode

- Poller is disabled entirely
- HTTP API returns a fixed snapshot from `fixtures/seed.json`
- SignalR sends no live updates (the connection establishes but emits
  nothing)
- Useful for: running `npm start` without backend dependencies, UI
  development, snapshot testing

## How to switch modes

### Locally (during development)

Edit `appsettings.Development.json` or set environment variable:

```bash
export SydneyPulse__Mode=offline
func start
```

### In dev environment

Update the Function App setting and restart:

```bash
az functionapp config appsettings set \
  --name sydney-pulse-func-dev \
  --resource-group sydney-pulse-rg-dev \
  --settings SydneyPulse__Mode=demo
```

### In prod (no redeploy)

Use Azure App Configuration so the change is immediate without restart:

```bash
az appconfig kv set \
  --name sydney-pulse-appconfig-prod \
  --key SydneyPulse:Mode \
  --value demo
```

The Functions are configured to refresh from App Configuration every
30 seconds, so the change takes effect within one polling cycle.

## Recording new fixtures

The fixture used by `demo` mode lives at
`functions/SydneyPulse.Tests/Fixtures/peak-hour-snapshot.json`. To
regenerate it from real data:

```bash
cd functions/SydneyPulse.Tests
dotnet run --project FixtureRecorder -- \
  --duration 5m \
  --output Fixtures/peak-hour-snapshot.json \
  --modes trains,buses,ferries
```

Run during actual Sydney peak hour (8 am or 5 pm local) to capture
realistic movement.

## Caveats

- `demo` mode timestamps are normalized to "now" so the dashboard's
  "X min ago" labels look right
- `offline` mode does not exercise SignalR; if you're testing SignalR
  changes, use `demo` mode instead
- Don't ship a build with `demo` or `offline` as the default for prod —
  the Bicep validation step enforces `live` for prod parameter files
