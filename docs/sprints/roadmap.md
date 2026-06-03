# Sydney Pulse — Delivery Roadmap

## Phase 1 — MVP (Sprint 1)
- Event-driven pipeline  
- Live dashboard  
- CI/CD  
- v0.1.0 release  

## Phase 2 — Production Hardening (Sprint 2)
- Multi-env IaC  
- Blue/green deploys  
- PR validation  
- Secretless architecture  
- v0.2.0 release  

## Phase 3 — Observability & Analytics (Sprint 3)
- Distributed tracing  
- KQL workbook  
- SLOs + alerts  
- Analytics + ops screens  
- v0.3.0 release  

## Phase 4 — Portfolio Polish (Sprint 4)
- ADRs  
- Cost model  
- MkDocs site  
- Blog post  
- Demo video  
- v1.0.0 release  

## Phase 5 — Intelligence (Sprint 5, conditional)
Triggered post-v1.0 only if predictive analytics adds material value
over the other long-term ideas. Archive shape was locked in at SP1-15
(ADR-0012) specifically to feed this sprint.
- Synapse Serverless / KQL external table over the Parquet archive  
- KQL `series_decompose_anomalies` baseline + SignalR `anomalyDetected` event  
- ONNX-in-Function PredictorFunction + Cosmos `predictions` container  
- `Prediction.v1` CloudEvent + analytics screen actual-vs-predicted view  
- ADR-0013 (ML approach choice)  
- v0.5.0 release  

## Always-on disciplines (cross-sprint)

These run continuously alongside sprint backlog items, not as discrete
deliverables. See `CLAUDE.md` "Daily rhythm" section for the full
framework (weekly ratio targets, reminder protocol, what counts as quiz/prep).

### Code review + interview-prep quiz
- Discipline: quiz every file group built that sprint via `reading-plan.xlsx`
- Owner: developer (verbal recall) + Claude (questioning + Word-doc append)
- Cadence: daily, ratio per `CLAUDE.md` "Daily rhythm" phase table
- Artefact: `SP1-14-Quiz-VehicleUpdate-ServiceAlert.docx` (grows each sprint; rename appropriately as scope grows beyond Sprint 1)
- Per-sprint backlog row: SP1-14, SP2-10, SP3-9, SP4-10, SP5-9 (sprint-long each)

### Architecture decisions
- New ADR every time a non-obvious decision lands, no exceptions

### Cost discipline
- Weekly cost-management review (`docs/cost-model.md` vs actual Azure billing)

## Long-term Ideas (Post v1.0)
- Mobile-friendly UI  
- Historical playback mode  
- Multi-city support  
