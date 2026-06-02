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

## Long-term Ideas (Post v1.0)
- Mobile-friendly UI  
- Historical playback mode  
- Multi-city support  
