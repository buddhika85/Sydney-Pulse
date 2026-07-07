# Sprint 2 — Production-grade CI/CD & IaC  
**Target:** 7 days  
**Goal:** Multi-environment deployments, Bicep modules, blue/green slots, PR validation, Key Vault + Managed Identity.

## Sprint Backlog

| # | Title | Description | Days |
|---|-------|-------------|------|
| 1 | Bicep modularization | Split main.bicep into modules | 1 |
| 2 | Multi-env params | dev.bicepparam + prod.bicepparam | 0.75 |
| 3 | Deployment slots | Add staging slot, per-slot settings | 0.5 |
| 4 | Blue/green workflow | Build → staging → smoke test → swap | 1.25 |
| 5 | PR validation | Lint, test, what-if, PR comment | 1 |
| 6 | Branch protection | CODEOWNERS, PR template, commitlint | 0.5 |
| 7 | Key Vault + MI | Move secrets to KV, enable MI | 1 |
| 8 | Reusable workflows | build-dotnet, build-angular, deploy-bicep | 0.5 |
| 9 | Sprint wrap | Tag v0.2.0, README updates, Loom demo | 0.5 |
| 10 | Code review + interview-prep quiz (always-on) | Sprint-long discipline per `CLAUDE.md` "Daily rhythm". Quiz this sprint's new code (Bicep modules, KV + MI, blue/green workflow, reusable workflows) + spaced-recall on Sprint 1 content. | sprint-long |
| 11 | Poller-side content diff + enrichment hardening (SP-18) | **Core (from 2026-06-16 DLQ analysis):** `Dictionary<alertId, contentHash>` in PollerFunction; publish only on hash change. State persisted as a single JSON blob in `sydpulsestordev` (read at start, write only when state changes). Cuts wire volume by ~99.6% per [debug story #13](../sp1-16-debug-stories.md). **Plus (from 2026-06-17 D.7 fixture analysis — debug stories #16, #17, #19):** (a) Alert enrichment hardening in `TfNswFeedClient.GetServiceAlertsAsync` — iterate `InformedEntity` instead of `.FirstOrDefault()`, return `null` on cache miss instead of falling back to raw `routeId` (story #19); (b) Vehicle speed unit fix in `TfNswFeedClient.cs:82` — GTFS-RT `Position.speed` is m/s but field is named `SpeedKmh`; rename `SpeedKmh` → `SpeedMps` OR add `* 3.6` conversion, with a pinning unit test (story #16); (c) Optional Poller-side `MaxStaleSeconds` filter (~30 min) — drop events before EG publish when `now - sourceTimestamp` exceeds the threshold, to break the parked-train re-upsert cycle (story #17). Acceptance: alert enrichment returns null (not raw IDs) for unenrichable entities; speed contract pinned by test; volume reduction verified via DLQ-exporter spike re-run. | 2.5 |
| 12 | Archiver data path smoke + Parquet manifest validation | Pre-flight de-risk before Sprint 3 Analytics view. Re-enable ArchiverFlush in Bicep + portal; run Poller for ≥1 hour; verify `pending/yyyy=.../events.jsonl` → `archive/.../*.parquet` + `_manifest.json`. DuckDB query the Parquet to confirm 24-column unified schema (ADR-0012). Deferred from SP1-16 D.5 + D.6 — Sprint 1 verifies only Commuter-facing paths. | 1 |
| 13 | Backend severity-comment cleanup | Doc-only fix: four files claim `Alert.Severity` looks like `"significant_delays"` / `"no_service"` / `"reduced_service"`, but runtime emits `"significantdelays"` / `"noservice"` / etc. because `TfNswFeedClient.cs:115` uses `alert.Effect.ToString().ToLowerInvariant()` on the GTFS-RT enum — underscores were never in the pipeline. Update comments on `ServiceAlert.cs`, `AlertDocument.cs`, `ArchiveEvent.cs:55`, `TfNswFeedClient.cs` to quote the actual no-underscore taxonomy or reference the GTFS-RT `Alert.Effect` enum directly. Frontend `AlertsPanelComponent.severityClass()` already uses the correct values (SP1-10). See `docs/sp1-10-debug-stories.md` #1. | 0.25 |
| 14 | Backend data-integrity hardening (SP1-10 smoke discoveries) | Three runtime bugs surfaced during SP1-10 first browser smoke test — backend types promise non-optional but runtime delivers null / undefined / empty string. Frontend added defensive guards to unblock the smoke; backend should enforce contracts at write time. See `docs/sp1-10-debug-stories.md` #3, #4, #5. **(a) `AlerterFunction.cs:54`** passes through `alert.Severity` as-is; upstream `ServiceAlert.Severity` can be null → Cosmos writes `severity: null` → frontend `severityClass()` crashed on first null. Guarantee non-null at write time (fallback to sentinel like `"unknowneffect"`) OR mark `AlertDocument.Severity` genuinely nullable in both type and Cosmos schema. **(b) `AlertDocument.Id`** sometimes lands as empty string — not explicitly set at write time; Cosmos accepts empty as valid `id`; frontend `@for track alert.id` hit NG0955 duplicate-key hard fail. Set explicitly to `alertId` verbatim OR composite `{routeShortName}:{alertId}` for partition-safe uniqueness. **(c) `StateWriterFunction` broadcasts Vehicles with missing `Position`** — GTFS-RT `VehiclePosition.Position` is optional per spec; backend passes lat/lng undefined through to SignalR; frontend `upsertMarker` crashed on `L.CircleMarker([undefined, undefined])`. Filter these before Cosmos write + SignalR broadcast OR flag `Vehicle.Latitude`/`Longitude` nullable and update the frontend TS type accordingly. Acceptance: 1-hour Poller run against the Angular dashboard produces zero runtime errors on first render with frontend defensive guards removed. Bundles cleanly with row 13 (same file cluster). | 1 |

**Total:** ~11.75 days dev + sprint-long always-on quiz discipline (SP2-10)

## Deliverables
- Multi-env IaC  
- Blue/green deploys  
- PR validation  
- Secretless architecture  
- v0.2.0 release  

## Acceptance Criteria
- Dev + prod deploy cleanly  
- Slot swap works  
- PRs blocked unless what-if passes  
- No secrets in app settings  
