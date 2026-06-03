# Sprint 5 — Intelligence (Conditional)

**Status:** Planning draft — not committed. Trigger: post-v1.0 if
predictive analytics adds material portfolio value over alternatives
(mobile UI, multi-city, historical playback).

**Target:** ~7 days (estimated; refine when committed)

**Goal:** Anomaly detection + predictive modelling on the archived
event stream, surfaced in the existing analytics screen. Demonstrate
two distinct ML/AI patterns (statistical KQL + custom ONNX) inside
the $20/month portfolio cost ceiling.

## Why this sprint exists

The Data Lake archive built in SP1-15 was shaped specifically to feed
an ML workload — Parquet, Hive partitions, three timestamps, explicit
columns (see ADR-0012). Sprint 5 is when we build the *consumer* of
that feature store — a Predictor Function — and prove the architecture
end to end.

## Sprint Backlog

| # | Title | Description | Days |
|---|-------|-------------|------|
| 1 | Synapse Serverless / KQL external table over Parquet | Make the archive queryable; pay-per-TB scanned | 0.5 |
| 2 | KQL `series_decompose_anomalies` baseline | Surface anomalies as a SignalR `anomalyDetected` event | 1 |
| 3 | Offline model training | Local Jupyter or Azure ML free tier (200 min/mo); export to ONNX | 1.5 |
| 4 | PredictorFunction (Timer-triggered) | Loads ONNX, reads recent archive partitions, writes to Cosmos `predictions` container | 1.5 |
| 5 | `Prediction.v1` CloudEvent + SignalR broadcast | New event type for fan-out (UI, possible anomaly-on-divergence) | 0.5 |
| 6 | `/analytics` actual-vs-predicted view | Comparison panel + per-route prediction error | 1 |
| 7 | ADR-0013 ML approach choice | Document why KQL + ONNX over Cognitive Services / Azure ML | 0.25 |
| 8 | Sprint wrap | Tag v0.5.0, blog or Loom, update `cost-model.md` | 0.5 |
| 9 | Code review + interview-prep quiz (always-on, if Sprint 5 is committed) | Sprint-long discipline per `CLAUDE.md` "Daily rhythm". Covers ML / ONNX / Synapse content if the sprint runs; otherwise inherits the active interviewing phase ratio. | sprint-long |

**Total:** ~6.75 days dev + sprint-long always-on quiz discipline (SP5-9, if sprint committed)

## ML approach — paths considered and chosen

Four options weighed. Selected: **A + B combined** — fits the $20
ceiling and demonstrates two distinct skill sets.

| Path | Marginal AUD/month (portfolio scale) | Portfolio value | Selected? |
|---|---|---|---|
| A. KQL `series_decompose_anomalies` on existing App Insights | ~$0 (within the 1 GB/day cap) | Good — KQL + statistical anomaly detection | ✅ |
| B. ONNX in-process inference in PredictorFunction (Consumption) | ~$2–3 | Strong — Functions-hosted ML, ONNX runtime, MLOps discipline | ✅ |
| C. Cognitive Services Anomaly Detector (managed, pay-per-call) | $0–25 depending on batching | OK — managed AI but less depth | ❌ |
| D. Azure ML Workspace + managed online endpoint | $60–100 (always-on VM) | Highest signal but breaks portfolio cost ceiling | ❌ |

## Cost implications

**Pre-Sprint 5 (Sprints 1–4): $0 marginal cost for ML readiness.** The
five ML-shape choices in the archive (3 timestamps + explicit Parquet
columns + `eventType`/`eventVersion` + `_manifest.json` per hour +
retained enums) add cents per month at portfolio scale, offset by the
Bicep lifecycle policy savings. See ADR-0012 for the design lock-in.

**Post-Sprint 5 (Path A + B) — portfolio scale:**

| Item | Marginal AUD/month |
|---|---|
| Synapse Serverless ad-hoc Parquet queries | ~$1–2 |
| KQL anomaly detection (within existing App Insights cap) | $0 |
| ONNX model file storage | <$0.01 |
| Cosmos `predictions` container writes (TTL ~1 h) | ~$0.50–1 |
| PredictorFunction execution (Consumption free grant) | ~$0 |
| Azure ML training (within free tier 200 min/month) | $0 |
| **Total marginal** | **~$2–3** |

Pre-Sprint 5 baseline (per `cost-model.md` current configuration):
$6–15/month. Post-Sprint 5 total: **~$8–18/month** — fits inside the
$20/month Sydney Pulse budget alert.

**Production scenario:** Sprint 5 adds ~$10–30/month on top of the
$583–1,563 production baseline — a rounding error at production scale.
See `cost-model.md` "Sprint 5 cost projection" section for line-item
detail.

## Out of scope

- Path D (managed Azure ML endpoint) — rejected on cost
- Real-time online learning (model updates as new data arrives)
- LLM-based natural-language summaries of the network
- Mobile push notifications for predicted disruptions
- Multi-tenant ML serving

## Deliverables

- PredictorFunction deployed (dev required, prod optional for cost control)
- KQL anomaly detection live
- `/analytics` actual-vs-predicted view
- ADR-0013 (ML approach choice)
- v0.5.0 tag
- Updated `cost-model.md`
- Blog post or Loom on the ML add

## Acceptance Criteria

- PredictorFunction running every 15 minutes, writing to Cosmos
  `predictions` container
- KQL anomaly queries returning results on real archived data
- Analytics screen showing measurable prediction error per route
- ADR-0013 published in `docs/adr/` and linked from `architecture.md`
- Total cost projection ≤ $20/month at portfolio scale (with budget
  review at v0.5.0 cutover if approaching ceiling)

## Decision triggers — before committing to Sprint 5

Re-evaluate after v1.0 (end of Sprint 4) whether to proceed based on:

- Did v1.0 land cleanly with no outstanding blockers?
- Is the archive accumulating ≥30 days of useful continuous volume?
- Has the portfolio outcome (interviews, learning goals) been met by
  Sprints 1–4 alone, or does Sprint 5 add material value?
- Are higher-priority alternatives (mobile UI, multi-city, historical
  playback) more valuable for the same effort?

## Risks & Mitigations

- **Model accuracy below useful threshold** → still demonstrates the
  ML pattern; document limitations honestly in the blog post.
- **Cost overrun** → run PredictorFunction in dev only, not prod
  (~50% saving on Sprint 5 marginal cost).
- **Training compute exceeds Azure ML free tier** → train locally on
  the developer laptop; export to ONNX; no Azure ML compute needed.
- **Schema evolution between Sprint 1 archive and Sprint 5 consumer**
  → ADR-0012 already committed us to versioned events; the v1 schema
  is stable for the archive's foreseeable horizon.

## Notes

- The archive shape was locked in at SP1-15 (ADR-0012) specifically
  with this sprint in mind. Sprint 5 is "build the consumer", not
  "build the feature store from scratch".
- Sprint 5 is **post-v1.0**. Version becomes v0.5.0 if pursued
  (additive minor release on top of v1.0); semantic-versioning
  decisions to be locked in if committed.
- This sprint deliberately combines statistical (KQL) and custom-model
  (ONNX) approaches to demonstrate both skill sets in the portfolio.
