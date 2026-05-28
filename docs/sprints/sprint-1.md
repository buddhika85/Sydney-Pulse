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
- Poller + State Writer + Alerter  
- HTTP API  
- Angular scaffolding  
- Live dashboard  
- CI/CD pipeline  
- v0.1.0 release  

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

**Total:** ~10 days

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
- CI/CD deploys on push  
- README includes architecture + live URL  

## Notes
If SignalR not stable by Day 6 → ship polling MVP and move SignalR to Sprint 2.
