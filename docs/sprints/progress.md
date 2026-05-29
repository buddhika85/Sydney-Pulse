# Sprint 1 — Progress Tracker

Living document. Updated as items complete or status changes. Authoritative
for current sprint state; `sprint-1.md` remains the authoritative scope spec.

**Sprint window:** started 2026-05-29 (~10 days per `sprint-1.md`)
**Goal:** Public live URL with full event-driven pipeline and Angular live
dashboard, tagged `v0.1.0`.

## Legend

⬜ pending · 🔄 in progress · ✅ done · ⚠️ blocked

## Backlog status

| #      | Item                              | Status | Started     | Done        | Commits             |
|--------|-----------------------------------|--------|-------------|-------------|---------------------|
| SP1-01 | Repo + Azure bootstrap            | ✅     | 2026-05-29  | 2026-05-29  | `eded4fa`, `ac1cd0a` |
| SP1-02 | SignalR de-risking spike          | ✅     | 2026-05-29  | 2026-05-29  | `d4629c9`           |
| SP1-03 | Bicep skeleton                    | ⬜     | —           | —           | —                   |
| SP1-04 | TfNswFeedClient                   | ⬜     | —           | —           | —                   |
| SP1-05 | Poller Function                   | ⬜     | —           | —           | —                   |
| SP1-06 | State Writer Function             | ⬜     | —           | —           | —                   |
| SP1-07 | Alerter chain                     | ⬜     | —           | —           | —                   |
| SP1-08 | HTTP API                          | ⬜     | —           | —           | —                   |
| SP1-09 | Angular scaffolding (deeper)      | ⬜     | —           | —           | —                   |
| SP1-10 | Live dashboard                    | ⬜     | —           | —           | —                   |
| SP1-11 | Landing page                      | ⬜     | —           | —           | —                   |
| SP1-12 | GitHub Actions CI/CD              | ⬜     | —           | —           | —                   |
| SP1-13 | Sprint wrap → v0.1.0              | ⬜     | —           | —           | —                   |

## SP1-01 — Repo + Azure bootstrap ✅

Closed 2026-05-29. Commits `eded4fa` (scaffolding) and `ac1cd0a` (CLAUDE.md
workflow doc).

Landed:

- Monorepo folders: `/functions`, `/web`, `/infra/{modules,parameters}`,
  `/.github/workflows`.
- .NET 8 solution with `SydneyPulse.Functions` (isolated worker),
  `SydneyPulse.Core`, `SydneyPulse.Tests` (xUnit). `global.json` pins SDK
  to `8.0.127`. Build clean.
- Angular 18 standalone app with Tailwind v3, SCSS, routing, no SSR.
  `ng build` clean (~246 kB bundle).
- Root `package.json` with commitlint + husky; `commit-msg` hook verified
  end-to-end on both initial commits.
- `README.md` skeleton linking to existing docs.
- Azure: `Sydney-Pulse-Montly-Budget` ($40/month) created in portal —
  verification still pending propagation in Cost Management API.

Out of scope (deferred or substituted):

- **GitHub Projects board** → using Jira instead per user preference.
- **gh CLI auth** → CLI installed but PATH not refreshed in bash; needed
  for SP1-12 (workflow dispatch).

## SP1-02 — SignalR de-risking spike ✅

Closed 2026-05-29. Risk gate cleared — end-to-end SignalR confirmed locally.
SP1-03 (Bicep skeleton) is unblocked.

Done:

- RG `sydney-pulse-rg-dev` created in `australiaeast`.
- SignalR Service `sydney-pulse-signalr-dev` provisioned: `Free_F1`,
  Serverless mode. Verified via `az signalr show` — hostname
  `sydney-pulse-signalr-dev.service.signalr.net`.
- NuGet `Microsoft.Azure.Functions.Worker.Extensions.SignalRService` 2.0.1
  added to `SydneyPulse.Functions.csproj`.
- `local.settings.json` updated with `AzureSignalRConnectionString` (live
  key, user-managed) and `Host: { CORS: "*", CORSCredentials: false }`.
- Security hardening: `.claude/settings.json` deny rules block Claude tools
  from reading, editing, or writing any `**/local.settings.json`.
- SignalR primary key rotated via Azure portal 2026-05-29.
- `NegotiateFunction.cs` — POST `/api/negotiate`, returns `SignalRConnectionInfo`
  serialised as camelCase JSON (`url`, `accessToken`) so the SignalR JS
  client recognises the Azure redirect.
- `SpikeFunction.cs` — POST `/api/spike`, broadcasts `{"text":"hello"}` to
  hub `spike` via `[SignalROutput]`.
- `spike.html` — minimal browser client; connects via negotiate, prints
  incoming `newMessage` payloads to a log panel.
- Manual test procedure documented in this file under SP1-02.
- New CLAUDE.md "Code comments" convention — intent-communicating comments
  on all code Claude writes; rolled into this bundle commit.

Non-obvious decisions landed:

- `func start` requires `dotnet clean` first — `WorkerExtensions.csproj`
  in `obj/` causes a "found 2 .csproj" error on plain `func start`.
- `spike.html` must be served via HTTP (not `file://`) to avoid `null`
  origin CORS rejection.
- `NegotiateFunction` must explicitly serialise response as
  `HttpResponseData` with camelCase JSON — isolated worker does not
  auto-serialise a raw return type to the HTTP response body.

### Manual test procedure (SP1-02 spike)

**Prerequisites**

- `AzureSignalRConnectionString` pasted into
  `functions/SydneyPulse.Functions/local.settings.json` (get the Primary
  Connection String from Azure Portal → SignalR Service →
  `sydney-pulse-signalr-dev` → Keys).
- `local.settings.json` has a top-level `Host` object (not a flat key):
  ```json
  "Host": { "LocalHttpPort": 7071, "CORS": "*", "CORSCredentials": false }
  ```
- Node.js available (for the static file server).

**Steps**

1. Start the Functions host (from
   `functions/SydneyPulse.Functions/`):
   ```
   dotnet clean SydneyPulse.Functions.csproj && func start
   ```
   Wait until the console lists both `negotiate` and `spike` endpoints.

2. In a second terminal, serve the spike page from the project root:
   ```
   python -m http.server 5500
   ```

3. Open `http://localhost:5500/spike.html` in a browser.

4. In a third terminal, fire the broadcast:
   ```
   curl.exe -X POST http://localhost:7071/api/spike
   ```

**Pass criteria**

| Check | Expected |
|-------|----------|
| Browser status line | "Connected to hub: spike" |
| Log panel after curl | `[HH:MM:SS.mmm] {"text":"hello"}` appears within ~1 s |
| `func start` console | No errors on the negotiate or spike invocations |

**Known gotchas**

- `func start` without `dotnet clean` first fails with "found 2 .csproj
  files" — the `WorkerExtensions.csproj` in `obj/` is the culprit.
- Opening `spike.html` directly as a `file://` URL causes CORS failure
  (`null` origin); always serve via `python -m http.server`.
- `NegotiateFunction` must serialize the response with camelCase keys
  (`url`, `accessToken`) — the SignalR JS client does a case-sensitive
  lookup to detect the Azure redirect.

## SP1-03 through SP1-13

Not started. Refer to `sprint-1.md` for scope and per-item description.

## Decisions logged

- **2026-05-29 — Tracking tool.** Jira boards, not GitHub Projects.
  `docs/sprints/*` remain authoritative for sprint backlog and progress.
- **2026-05-29 — Collaboration pattern.** Strict step-by-step. User runs
  every Azure mutation (portal or CLI); Claude provides instructions and a
  read-only verification command. No parallel code work; one file change
  per turn, announced before edit. Documented in `CLAUDE.md` under "Working
  with Claude Code".
- **2026-05-29 — Service Bus reuse confirmed.** Will reuse
  `devpulse-service-bus` in `DevPulseRG` (Standard tier) per ADR-0003. Real
  names go only in `infra/parameters/dev.bicepparam` (SP1-03); docs stay
  generic.
- **2026-05-29 — Secrets policy.** `local.settings.json` is gitignored
  AND permission-denied from Claude tools (`.claude/settings.json`) to
  prevent leakage via the harness's file-modification reminder mechanism.
  SP1-03 will layer in Key Vault references for both cloud and local.

## Risks / open items

| Risk                                                   | Mitigation                                                                | Owner   |
|--------------------------------------------------------|---------------------------------------------------------------------------|---------|
| `gh` CLI not on bash PATH                              | Reopen terminal or fix PATH; needed for SP1-12 (workflow dispatch)        | User    |
| Azure budget alert API propagation pending             | Re-list with `az consumption budget list` after ~10 min                   | User    |
| TfNSW API key not yet in Key Vault                     | Wait for SP1-03 (Bicep creates KV), then `az keyvault secret set`         | User    |
| ADR-0003 still references placeholder Service Bus name | Resolves in SP1-03 when `dev.bicepparam` is authored (Option B pattern)   | Claude  |
| SignalR Free SKU caps (20 conns, 20k msgs/day)         | Acceptable per ADR-0008; load-test forbidden                              | —       |
| TfNSW API quota (5 rps, 60k/day)                       | Polly retry policy in TfNswFeedClient (SP1-04); never bypass              | Claude  |

## Update protocol

When an item closes:

1. Flip the row's status to ✅, fill in `Done` date and commit hashes.
2. Add a short prose section above with what landed and any deferrals.
3. Move follow-ups to "Risks / open items" if non-blocking.

When an item is blocked:

1. Flip to ⚠️ with a note in "Risks / open items" describing the blocker
   and what unblocks it.

## Next session handoff (2026-05-29 EOD)

SP1-02 closed. SP1-03 (Bicep skeleton) is next.

### Where we are

SP1-02 complete and verified end-to-end. SignalR risk gate cleared.
All code committed in the SP1-02 bundle commit (see git log).

### Resume sequence for SP1-03

1. Follow session start protocol per `CLAUDE.md`.
2. Start SP1-03 — Bicep skeleton. Refer to `sprint-1.md` for scope.
   Key constraint: reuse existing Service Bus namespace (ADR-0003);
   real namespace name goes only in `infra/parameters/dev.bicepparam`.

### Standing operating rules

- User runs all Azure mutations. Claude provides instructions and
  read-only verification only.
- One file change per turn. Announce file and reason before editing.
- Claude cannot read/write `**/local.settings.json` (deny rule in
  `.claude/settings.json`).
- Windows / PowerShell — single-line commands only when asking user to paste.
- TfNSW API key goes into Key Vault during SP1-03, never into app settings.
- Code comments convention active — intent-communicating comments on all
  source files Claude writes.
