# Sprint 1 — MVP with Live URL  
**Target:** 7–9 days  
**Goal:** Deploy a working MVP to Azure Static Web Apps with the full event-driven pipeline operational:  
Poller → Event Grid → State Writer → Cosmos → HTTP API + SignalR → Angular Live Dashboard

## Sprint Goal
Deliver a public live URL showing real-time vehicle updates from TfNSW, backed by a complete event-driven architecture deployed via Bicep and CI/CD.

## Scope
- Repo + Azure bootstrap  
- Infrastructure skeleton  
- TfNSW client library  
- Poller + State Writer + Alerter + Archiver  
- HTTP API  
- Angular scaffolding  
- Live dashboard  
- CI/CD pipeline  
- v0.1.0 release  
- Developer code review + ownership handover  
- ADR-0012 (archive-as-ML-feature-store design lock-in for future Sprint 5)  

## Sprint Backlog

| # | Title | Description | Days |
|---|-------|-------------|------|
| 1 | Repo + Azure bootstrap | Create monorepo, init .NET + Angular, GitHub Projects, budget alert | 0.5 |
| 2 | SignalR de-risking spike | Provision SignalR Free, test hello-world Function + HTML | 0.5 |
| 3 | Bicep skeleton | Deploy Storage, Functions, Cosmos, Event Grid, SignalR, SWA, KV | 1 |
| 4 | TfNswFeedClient | HttpClient + Polly, GTFS decoding, static caching, tests | 1 |
| 5 | Poller Function | Timer-triggered, publishes VehicleUpdate.v1 events | 0.5 |
| 6 | State Writer Function | Event Grid trigger, upsert to Cosmos | 0.5 |
| 7 | Alerter chain | Event Grid → Service Bus → SignalR | 1 |
| 8 | HTTP API | /vehicles, /alerts, /routes, /negotiate | 1 |
| 9 | Angular scaffolding | ng new, install libs, create routes | 0.5 |
| 10 | Live dashboard | Leaflet map, SignalR, alerts panel, filters | 2 |
| 11 | Minimal landing page | Hero, CTA, simple architecture SVG | 0.75 |
| 12 | GitHub Actions | Lint, test, deploy infra + app | 0.75 |
| 13 | Sprint wrap | Tag v0.1.0, README, Loom demo, LinkedIn | 0.5 |
| 14 | Developer code review + ownership | Read all SP1-01→08 files using reading plan, rewrite priority files, pass Claude interrogation | 2 |
| 15 | Archiver Function | Event Grid → Parquet batched 5 min / 10K events to Data Lake Gen 2 `archive/` container. Durable Functions checkpointing to survive mid-batch crashes. Hive-partitioned layout (`yyyy=.../MM=.../dd=.../HH=...`). ML-ready schema: 3 timestamps (`vehicleTimestamp`, `publishedAt`, `archivedAt`), explicit Parquet columns (no JSON blob), `eventType` + `eventVersion` columns, `_manifest.json` per partition hour. Bicep lifecycle policy (Hot→Cool 30d, Cool→Cold 90d) to cap long-term cost. Fix the placeholder webhook URL on the EG `archiver` subscription. Unit tests + `docs/testing.md` inventory. New ADR-0012 locking in the archive-as-ML-feature-store design for future Sprint 5. | 2 |

**Total:** ~14 days

## Risks & Mitigations
- **SignalR auth issues** → fallback to polling by Day 6  
- **GTFS decoding complexity** → reuse libraries  
- **Dashboard complexity** → defer polish to Sprint 4  

## Deliverables
- Live URL  
- Event-driven pipeline  
- Live dashboard  
- CI/CD pipeline  
- v0.1.0 release  
- Demo video  
- LinkedIn post  

## Acceptance Criteria
- Real-time updates visible  
- Alerts panel functional  
- API endpoints correct  
- Cosmos updated continuously  
- Archive Parquet files being written every 5 minutes to Data Lake Gen 2 (`archive/yyyy=.../MM=.../dd=.../HH=...`)  
- Storage lifecycle policy provisioned (Hot 0–30 d, Cool 30–90 d, Cold 90+ d)  
- ADR-0012 published and linked from architecture.md  
- CI/CD deploys on push  
- README includes architecture + live URL  

## Notes
If SignalR not stable by Day 6 → ship polling MVP and move SignalR to Sprint 2.
