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
| 9 | Angular scaffolding | ng new, install libs, create routes. **Frontend unit tests deferred to [SP-21](https://gsoft85512.atlassian.net/browse/SP-21)** — keeps Sprint 1 focused on shipping the live URL by Jul 1; backend already at 55 tests, target roles are .NET-senior with Angular secondary | 0.5 |
| 10 | Live dashboard | Leaflet map, SignalR, alerts panel, filters | 2 |
| 11 | Minimal landing page | Hero, CTA, simple architecture SVG | 0.75 |
| 12 | GitHub Actions | Lint, test, deploy infra + app | 0.75 |
| 13 | Sprint wrap + portfolio refresh | Tag v0.1.0, README with live URL + architecture, Loom demo, LinkedIn post. **CV refresh** at `C:\BUDDHIKA\2026 July\CV\CV - 2026 - After SydneyPulse Sprint 1.docx` + `.md`: (a) replace placeholder URL (`sydney-pulse-web-prod.azurestaticapps.net`) with the actual deployed Static Web App URL, (b) confirm GitHub repo URL, (c) swap TfNSW-quota proxies for measured numbers captured from App Insights once Sprint 1 is stable in dev — p50/p99 Cosmos single-partition read latency, SignalR fanout latency (publish → client receive), Event-Grid-to-client p99, sustained vehicle-update throughput (events/sec), GitHub Actions build → deploy cycle time. Rebuild via `build-cv-docx.ps1`. Reference: `memory/project_sp1_close_cv_followups.md`. | 1 |
| 14 | Code review + interview-prep quiz (always-on) | Sprint-long discipline: quiz every file group (SP1-01→15) using `reading-plan.xlsx`; verbal recall on PollerFunction / StateWriter / Alerter / HTTP API / DI / Tests / Bicep groups; daily mix per `CLAUDE.md` "Daily rhythm" (~30% of working time this sprint) | sprint-long |
| 15 | Archiver Function | Event Grid → Parquet batched 5 min / 10K events to Data Lake Gen 2 `archive/` container. Durable Functions checkpointing to survive mid-batch crashes. Hive-partitioned layout (`yyyy=.../MM=.../dd=.../HH=...`). ML-ready schema: 3 timestamps (`vehicleTimestamp`, `publishedAt`, `archivedAt`), explicit Parquet columns (no JSON blob), `eventType` + `eventVersion` columns, `_manifest.json` per partition hour. Bicep lifecycle policy (Hot→Cool 30d, Cool→Cold 90d) to cap long-term cost. Fix the placeholder webhook URL on the EG `archiver` subscription. Unit tests + `docs/testing.md` inventory. New ADR-0012 locking in the archive-as-ML-feature-store design for future Sprint 5. | 2 |
| 16 | Backend visibility (manual deploy + smoke) | Manual `func azure functionapp publish` to `sydney-pulse-func-dev` — Function App infra already provisioned in SP1-03, code never deployed. Wire deferred Event Grid webhook URLs (`state-writer`, `archiver` subscriptions) now that the Function App URL exists — either via `az` CLI or Bicep re-deploy with `funcAppDefaultHostname` populated. End-to-end smoke pass in real Azure: Poller traces every 30s in App Insights, State Writer upserting Cosmos `vehicles` docs, Alerter consuming Service Bus + writing `alerts` docs, HTTP API curl returning real data, SignalR negotiate + live broadcast via `spike.html`. **Descope (2026-06-16):** ArchiverIngest + ArchiverFlush smoke (originally D.5 + D.6) pulled out to Sprint 2 [SP-19](https://gsoft85512.atlassian.net/browse/SP-19) — pre-Analytics de-risk; Sprint 1 verifies only Commuter-facing paths. Evidence pack at `docs/runbooks/dev-smoke-evidence.md` (screenshots + KQL queries + curl output) + reproducible deploy recipe at `docs/runbooks/manual-deploy-dev.md` — both become inputs to SP1-12 (CI/CD). De-risks SP1-12 by surfacing deploy issues with a human in the loop. Produces interview-grade artefacts: live App Insights traces, Cosmos Data Explorer with real data, Parquet evidence — directly answers "show me something you built running". 2-3 quiz Q&As captured into the Word doc + `interview-prep.md`. **Ordering:** runs before SP1-09 — no frontend dependency (SignalR smoke uses `spike.html` from SP1-02). Scope explicitly excludes prod RG / slot swap (deferred to SP2-03 / SP2-04) and CI/CD automation (SP1-12). **Detailed 8-phase execution plan: [backend-manual-deploy-plan.md](backend-manual-deploy-plan.md).** | 1.5 |

**Total:** ~14.5 days dev + sprint-long always-on quiz discipline (SP1-14)

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
- Refreshed CV (`.docx` + `.md`) at `C:\BUDDHIKA\2026 July\CV\` with measured perf numbers and the real live URL — application-ready for the Aug 1 job-hunt window  

## Acceptance Criteria
- Real-time updates visible  
- Alerts panel functional  
- API endpoints correct  
- Cosmos updated continuously  
- Archive Parquet files being written every 5 minutes to Data Lake Gen 2 (`archive/yyyy=.../MM=.../dd=.../HH=...`)  
- Storage lifecycle policy provisioned (Hot 0–30 d, Cool 30–90 d, Cold 90+ d)  
- ADR-0012 published and linked from architecture.md  
- Backend smoke pass: every Function shows ≥1 successful invocation in dev App Insights; `/api/vehicles` returns real TfNSW data; SignalR client receives at least 1 live broadcast (SP1-16). **Archiver Parquet + manifest evidence descoped to [SP-19](https://gsoft85512.atlassian.net/browse/SP-19) (Sprint 2 row 12)** — pre-Analytics de-risk  
- Manual-deploy runbook reproducible from `docs/runbooks/manual-deploy-dev.md` — second deploy from the runbook succeeds cleanly (SP1-16)  
- CI/CD deploys on push  
- README includes architecture + live URL  
- CV no longer contains the placeholder URL (`sydney-pulse-web-prod.azurestaticapps.net`); Sydney Pulse bullets carry at least one measured perf number pulled from real App Insights / Cosmos / SignalR diagnostics (SP1-13)  

## Notes
If SignalR not stable by Day 6 → ship polling MVP and move SignalR to Sprint 2.
