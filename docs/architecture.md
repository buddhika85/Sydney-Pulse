# Architecture

System overview, dataflow, and per-component responsibilities for Sydney Pulse.

## At a glance

```
TfNSW Open Data (GTFS-realtime + static)
        │
        ▼
   Poller Function (timer 30s)
        │
        ▼
   Event Grid topic (transit-events)
        │
        ├─────────────────┬──────────────────┐
        ▼                 ▼                  ▼
  State Writer Fn   Service Bus topic   Archiver Fn
        │           (alerts only)            │
        ▼                 ▼                  ▼
   Cosmos DB        Alerter Fn          Data Lake Gen2
   (live state)          │              (historical)
        │                ▼                  │
        │          SignalR Service          │
        │                │                  │
        ▼                ▼                  ▼
        └────── HTTP API Function ◄─────────┘
                        │
                        ▼
              Static Web App (Angular)
              /, /live, /analytics, /ops
```

## Component responsibilities

### Poller Function

Timer-triggered every 30 seconds. Calls `TfNswFeedClient` for each transport
mode (Sydney Trains, Buses, Sydney Ferries, Sydney Metro, Light Rail Inner
West), decodes GTFS-realtime protobuf payloads, enriches with route metadata
from cached GTFS static feeds, and publishes one CloudEvent per vehicle
update or service alert to the Event Grid custom topic.

- Trigger: TimerTrigger `*/30 * * * * *`
- Dependencies: TfNSW API key (from Key Vault via Managed Identity),
  Event Grid publish endpoint (from Key Vault)
- Output: `VehicleUpdate.v1` and `ServiceAlert.v1` CloudEvents
- Failure handling: Polly retry with exponential backoff; failed batches
  logged but do not block subsequent polls
- Concurrency: singleton (one instance only) to avoid duplicate publishes

### Event Grid topic

Custom topic named `transit-events`. Receives all events from the Poller
and fans out to three subscribers using event type filtering.

- Subscription `state-writer`: matches `VehicleUpdate.v1`, delivers to
  the State Writer Function
- Subscription `alerter`: matches `ServiceAlert.v1`, delivers to the
  Service Bus topic
- Subscription `archiver`: matches all event types, delivers to the
  Archiver Function

Delivery guarantees: at-least-once. Consumers are idempotent.

### State Writer Function

Event Grid trigger consuming `VehicleUpdate.v1` events. Upserts into Cosmos
DB partitioned by `routeShortName`. Idempotent via composite key
(`vehicleId` + `timestamp`).

- Trigger: EventGridTrigger
- Cosmos container: `vehicles`, partition key `/routeShortName`
- Stale write protection: skips upsert if incoming `timestamp` < stored
- Document TTL: 5 minutes (older documents auto-purged by Cosmos)

### Service Bus topic + Alerter Function

Service Bus topic on the pre-existing Standard namespace. Topic named
`sydney-pulse-alerts`. One subscription `alerter-sub`. Standard tier
supports sessions but ADR-0010 chooses not to use them.

The Alerter Function consumes from the subscription, transforms each
`ServiceAlert.v1` event into a UI-friendly payload, and broadcasts via
SignalR to the `alerts` group.

- Trigger: ServiceBusTrigger
- Output binding: SignalR Service output binding (HTTP-based)
- Dead-letter: max delivery count 5; manual reprocessing via runbook

### Cosmos DB

Serverless account (ADR-0002). Two containers:

- `vehicles` — partition key `/routeShortName`. Stores latest position
  per vehicle. TTL 5 minutes.
- `alerts` — partition key `/routeShortName`. Stores active alerts.
  TTL 24 hours.

Indexing: default indexing policy, excluding `lat` and `lon` from the
index (geospatial queries not needed; cuts RU usage 30%).

### Data Lake Storage Gen2

Archive of raw event payloads for historical analytics. Hot tier, ~3 GB
expected steady-state.

- Container: `archive`
- Layout: `archive/yyyy=2026/MM=05/dd=28/HH=14/events.parquet`
- Format: Parquet, batched every 5 minutes by the Archiver Function
- Retention: indefinite (manual deletion only)

### Archiver Function

Event Grid trigger receiving all event types. Batches in-memory for up to
5 minutes or 10,000 events, whichever comes first, then writes a Parquet
file to Data Lake. Uses durable function checkpointing to survive crashes
mid-batch.

### HTTP API Function

REST API exposing read access for the Angular app.

- `GET /api/vehicles?mode=trains` — current vehicle state
- `GET /api/alerts` — currently active alerts
- `GET /api/routes` — route metadata (cached, served from in-memory cache)
- `GET /api/analytics/reliability?routeShortName=T1&days=30` — proxies KQL
  query against Application Insights
- `GET /api/ops/slos` — current SLO values from KQL
- `POST /api/negotiate` — SignalR connection token endpoint

Authentication: anonymous read; CORS-enabled for the Static Web App origin.
Rate limiting: Function-level concurrency cap of 10 concurrent invocations.

### SignalR Service

Free SKU (ADR-0008). Two groups:

- `vehicles` — broadcast on every vehicle position update
- `alerts` — broadcast on every new or updated service alert

Connection auth: clients call `/api/negotiate` to receive a short-lived
token. The negotiate endpoint is anonymous (no user identity tracked).

### Angular frontend

Four routes (ADR-0007 added the operations view):

- `/` — Landing page (static content)
- `/live` — Commuter dashboard with Leaflet map and SignalR live updates
- `/analytics` — Reliability heatmaps and historical analysis
- `/ops` — SLO dashboard, distributed traces, recent deployments

State management: RxJS services. No NgRx. The SignalR connection is exposed
as `Observable<VehicleUpdate>` which components subscribe to independently.

## Data contracts

See `/docs/api.md` for HTTP endpoint schemas and SignalR event payloads.

## Operational concerns

- Observability: every Function emits to Application Insights via the
  SDK. 5% sampling, 1 GB/day cap. Distributed tracing via W3C
  TraceContext propagates through Event Grid metadata.
- Identity: all service-to-service auth via system-assigned Managed
  Identities. No connection strings in app settings (everything via
  Key Vault references).
- Secrets: Key Vault `sydney-pulse-kv-<env>`. Functions have
  `Key Vault Secrets User` role on the appropriate vault.
- Network: no VNet integration. All services use public endpoints with
  Managed Identity auth. VNet would push us to Premium plans (ADR-0006
  considered and rejected this).

## Failure modes and mitigations

| Failure | Detection | Mitigation |
|---|---|---|
| TfNSW API down | Poller HTTP errors logged | Stale data served from Cosmos until upstream recovers |
| Event Grid backed up | Failed delivery metric | Retry with exponential backoff up to 24h |
| Cosmos throttling (429) | App Insights dependency telemetry | Polly retry in State Writer; bursty workload smoothed by Event Grid buffer |
| Service Bus DLQ filling | Alert rule on DLQ depth | Runbook in `/docs/runbooks/incident-response.md` |
| SignalR connection cap (20) | Increased negotiate failures | Documented as portfolio-scale constraint; load shed gracefully |
| Function cold start | App Insights operation latency | Acceptable at 30s polling cadence; not user-facing on read path |
