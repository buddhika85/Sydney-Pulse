# Sydney Pulse

Real-time disruption intelligence for Sydney public transport. Event-driven Azure architecture built on TfNSW open data.

**Status:** Sprint 1 in progress — MVP scaffold. See [docs/sprints/sprint-1.md](docs/sprints/sprint-1.md).

## What this is

A portfolio project demonstrating an end-to-end event-driven cloud system on Azure: polling TfNSW GTFS-realtime feeds, fanning out through Event Grid, persisting state in Cosmos, broadcasting live updates over SignalR to an Angular dashboard. Built to showcase AZ-400 / DevOps Engineer skills.

## Quick links

- **Architecture overview** — [docs/architecture.md](docs/architecture.md)
- **Architecture diagram (Mermaid)** — [docs/diagrams.md](docs/diagrams.md)
- **API contracts (HTTP + SignalR)** — [docs/api.md](docs/api.md)
- **Architecture decisions** — [docs/adr/](docs/adr/)
- **Operational runbooks** — [docs/runbooks/](docs/runbooks/)
- **Cost model** — [docs/cost-model.md](docs/cost-model.md)
- **Build / demo / offline modes** — [docs/modes.md](docs/modes.md)
- **Sprint plans** — [docs/sprints/](docs/sprints/)
- **Project rules for Claude Code** — [CLAUDE.md](CLAUDE.md)

## Tech stack

- **Backend:** .NET 8 Azure Functions (isolated worker)
- **Frontend:** Angular 18 (standalone components, RxJS, Tailwind, Leaflet)
- **Infrastructure:** Bicep
- **CI/CD:** GitHub Actions
- **Hosting:** Azure Static Web Apps + Azure Functions Consumption
- **Data:** Cosmos DB Serverless, Data Lake Gen2, Event Grid, Service Bus, SignalR Service

## Repository layout

```
/functions/   .NET solution — Functions host, Core library, xUnit tests
/web/         Angular standalone app
/infra/       Bicep modules and per-environment parameter files
/docs/        Architecture reference, ADRs, runbooks, sprint plans
/.github/     CI/CD workflows
```

## Getting started

Full setup steps land in this README as Sprint 1 progresses. See [CLAUDE.md](CLAUDE.md) for tooling expectations and conventions.

## Live URL

_To be added after first Sprint 1 deploy._
