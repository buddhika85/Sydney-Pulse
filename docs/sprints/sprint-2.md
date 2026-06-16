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
| 11 | Reduce alert event volume via Poller-side content diff | `Dictionary<alertId, contentHash>` in PollerFunction; publish only on hash change. State persisted as a single JSON blob in `sydpulsestordev` (read at start, write only when state changes). Cuts wire volume by ~99.6% per 2026-06-16 DLQ analysis (see [debug story #13](../sp1-16-debug-stories.md)). | 1.5 |

**Total:** ~8.5 days dev + sprint-long always-on quiz discipline (SP2-10)

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
