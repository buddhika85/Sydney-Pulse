# Sydney Pulse

Real-time disruption intelligence for Sydney public transport. Event-driven
Azure architecture built from Transport for NSW open data. Portfolio project
targeting AZ-400 / DevOps Engineer roles.

## Tech stack

- Backend: .NET 8 isolated-worker Azure Functions (C#)
- Frontend: Angular 18+ standalone components, RxJS, Tailwind CSS, Leaflet
- Infrastructure: Bicep (NOT Terraform — see ADR-0004)
- CI/CD: GitHub Actions (NOT Azure Pipelines)
- Hosting: Azure Static Web Apps + Azure Functions Consumption plan

## Project layout

```
/functions/   .NET solution
  SydneyPulse.Functions/      Azure Functions host
  SydneyPulse.Core/           Models, TfNswFeedClient, business logic
  SydneyPulse.Tests/          xUnit tests
/web/         Angular app
/infra/       Bicep modules + parameter files
/docs/        Reference docs and ADRs
/.github/     CI/CD workflows including reusable templates
```

## Key architectural decisions

Full reasoning lives in `/docs/adr/`. Critical decisions to know upfront:

- **Event Grid + Service Bus topic** for messaging (ADR-0001). Event Grid
  fans out vehicle updates; Service Bus topic carries only ServiceAlert
  events via subscription filter. Reuses an existing Standard namespace.
- **Cosmos DB Serverless**, not provisioned RU/s (ADR-0002). Partition key
  is `routeShortName`. Free tier was unavailable on this subscription.
- **Reused existing Service Bus Standard namespace** (ADR-0003). Add new
  topics; never modify namespace-level config.
- **Bicep over Terraform** (ADR-0004) for tight Azure alignment.
- **Angular over React** (ADR-0005) for .NET ecosystem alignment.
- **Deployment slots, not feature flags** for blue/green (ADR-0006).
- **SRE is a first-class actor** (ADR-0007). Operations is a product surface.
- **SignalR Free SKU** sufficient at portfolio scale (ADR-0008).
- **GTFS static feeds cached 1 hour** in memory (ADR-0009).
- **Alert ordering is per-route best-effort**, not strict global (ADR-0010).

## Conventions

- **Commits**: Conventional Commits (`feat:`, `fix:`, `chore:`, `docs:`,
  `refactor:`, `test:`). Enforced by commitlint in pre-commit hook.
- **Branches**: GitHub Flow off `main`. Feature branches, PR + squash merge.
- **Resource naming**: `sydney-pulse-<service>-<env>`, e.g.
  `sydney-pulse-func-prod`, `sydney-pulse-cosmos-dev`.
- **C#**: PascalCase types and methods. `dotnet format` on save. Top-level
  statements allowed only in `Program.cs`.
- **TypeScript**: strict mode, no `any`, kebab-case filenames.
- **Bicep**: kebab-case module names. Parameters in camelCase.
- **Code comments**: Code Claude writes (C#, TypeScript, Bicep, YAML,
  shell, HTML — any source file) must include short, simple comments that
  communicate intent. Keep comments short and focused on intent.
  Each file gets a brief header explaining its purpose.
  Each non‑obvious block gets a one‑line “why” comment.
  No long explanations. No noise. Just enough for future readers
  (and future Claude sessions) to understand the intent quickly.
  Example: SydneyPulse.Functions.Functions.NegotiateFunction.cs file

## What lives where

- TfNSW API client: `functions/SydneyPulse.Core/TfNsw/TfNswFeedClient.cs`
- Route metadata cache: in-memory, 1h TTL, inside `TfNswFeedClient`
- Event Grid schemas: `functions/SydneyPulse.Core/Events/` (as records)
- Cosmos models: `functions/SydneyPulse.Core/Cosmos/`
- Bicep modules: `infra/modules/{network,messaging,compute,data,observability}.bicep`
- Environment parameters: `infra/parameters/{dev,prod}.bicepparam`
- Reusable workflows: `.github/workflows/reusable/`

## Environments

- **dev** — `sydney-pulse-rg-dev`. Auto-deploys from `main`. Free SKUs.
- **prod** — `sydney-pulse-rg-prod`. Manual approval before slot swap.
- **No staging** — slot swap on prod Function App fills that role.

## Non-obvious constraints (read before changing related code)

- TfNSW API key lives in **Key Vault**, accessed via system-assigned
  Managed Identity. Never put it in Function App settings directly.
- Service Bus Standard namespace is **pre-existing** and shared with other
  workloads in this subscription. Only add new topics/queues; never modify
  namespace-level configuration.
- Application Insights sampling is fixed at **5%** with a **1 GB/day cap**.
  Disabling sampling without coordination burns through the free tier in days.
- SignalR Free SKU caps at **20 concurrent connections** and **20k messages
  per day**. Don't load-test the live dashboard.
- Cosmos Serverless is billed **per RU**. Hot loops without throttling can
  spike a $20+ bill in hours.
- TfNSW API rate limit is **5 requests per second**, 60k per day. The
  `TfNswFeedClient` has Polly retry policy; do not bypass it.
- `route_id` is internal (e.g. `NTH_1a`); `route_short_name` is user-facing
  (e.g. `T1`). Always group by `route_short_name` for UI and partitioning.

## How to verify things work

- Run all tests: `dotnet test functions/SydneyPulse.sln && cd web && npm test`
- Lint everything: `dotnet format --verify-no-changes && cd web && npm run lint`
- Run Functions locally: `cd functions/SydneyPulse.Functions && func start`
  (requires Azurite + appropriate environment variables loaded)
- Run Angular locally: `cd web && npm start`
- Deploy to dev: `gh workflow run deploy.yml -f environment=dev`
- Tail prod App Insights logs: see `/docs/runbooks/deploy.md`

## When in doubt

- `/docs/architecture.md` — system overview and dataflow
- `/docs/api.md` — HTTP and SignalR contracts
- `/docs/adr/` — the "why" behind every major decision
- `/docs/cost-model.md` — tier choices and scaling notes
- `/docs/modes.md` — build vs demo mode behaviour
- `/docs/runbooks/` — deploy, rollback, incident response

## Working with Claude Code

Strict step-by-step collaboration. No parallel work.

### Session start protocol

On every fresh Claude Code session, before acting on the first user
request:

1. Read `docs/sprints/progress.md` — current sprint state, what's done,
   what's in progress, what's blocked.
2. Read the current sprint file (e.g., `docs/sprints/sprint-1.md`) —
   scope and acceptance criteria for the active sprint.
3. Run `Glob` on `docs/**/*.md` to know what other docs exist (ADRs,
   runbooks, architecture, api, cost-model, modes).
4. Read additional docs on demand when the task touches their area.
   Always mention the relevant ADR number when starting work on an
   established decision area.

Briefly report after step 3: active sprint, last completed item, next
pending item, any blocking risks from `progress.md`.

Memory files in `~/.claude/projects/.../memory/` and this `CLAUDE.md`
are auto-loaded — no explicit read needed for those.

### Azure changes — verification cycle

The human developer runs every command that creates, modifies, or
deletes Azure resources. Claude provides instructions only and never
executes them.

For each Azure change:
1. Claude gives Azure Portal click-path (preferred) or a single-line
   PowerShell command, with what / cost / why.
2. Developer executes the steps.
3. Claude gives a read-only verification command
   (`az ... show`, `az ... list`).
4. Developer runs it, pastes the output.
5. Both confirm the resource matches expectations before the next step.

Read-only `az` queries (list, show, query) are safe for Claude to run
directly — no need to ask.

### Code changes

- One file at a time. Claude announces which file is being modified
  and why *before* editing.
- Developer reviews each change before Claude proceeds.
- No batching unrelated edits in a single turn.

### Feature branches and PRs (SP1-05 onwards)

Every sprint item from SP1-05 forward gets its own feature branch and PR.
Never commit directly to `main` for sprint work.

**Branch naming:** `feat/<ticket-id>-<short-description>` (kebab-case).
Example: `feat/sp1-05-poller-function`

**Cycle for each sprint item:**

1. **Create the branch** before touching any files:
   `git checkout -b feat/<ticket-id>-<short-description>`
2. **Implement** — commit as you go using Conventional Commits.
   Each logical step gets its own commit on the branch.
3. **Push the branch:**
   `git push -u origin feat/<ticket-id>-<short-description>`
4. **Open a PR** — Claude uses the GitHub MCP tool (`create_pull_request`)
   to create the PR with summary bullets, test plan checklist, and ticket
   reference. Do not use `gh pr create` — MCP is preferred when available.
5. **Squash merge** — MCP `merge_pull_request` returns 403 on this repo;
   developer runs locally:
   `gh pr merge <number> --squash --delete-branch`
6. **Post-merge** — developer runs:
   `git checkout main && git pull origin main`
   Claude then updates `docs/sprints/progress.md` (flip row to ✅, add prose
   section, update handoff) and commits directly to `main` as a housekeeping
   commit.

**Housekeeping commits** (gitignore, docs fixes, progress.md updates unrelated
to a sprint feature) may go directly to `main` without a PR.

### PowerShell paste gotcha

Multi-line commands with backtick continuation often break when pasted
into PowerShell. Prefer single-line commands when handing the developer
something to run.

### Useful slash commands

- `/add-dir docs/adr` — pull all ADRs into the working set
- `/add-dir infra` — pull all Bicep files when doing infrastructure work
- Always mention the relevant ADR number when starting a task that
  touches an established decision area.
