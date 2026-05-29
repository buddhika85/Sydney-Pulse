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
| SP1-02 | SignalR de-risking spike          | 🔄     | 2026-05-29  | —           | —                   |
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

## SP1-02 — SignalR de-risking spike 🔄

Started 2026-05-29. **Day-1 risk gate** per `sprint-1.md` — must
demonstrate end-to-end SignalR by Day 6 or fall back to a polling MVP for
v0.1.0.

Done:

- RG `sydney-pulse-rg-dev` created in `australiaeast`.
- SignalR Service `sydney-pulse-signalr-dev` provisioned: `Free_F1`,
  Serverless mode. Verified via `az signalr show` — hostname
  `sydney-pulse-signalr-dev.service.signalr.net`.
- NuGet `Microsoft.Azure.Functions.Worker.Extensions.SignalRService` 2.0.1
  added to Functions project (one-line `.csproj` diff, ~25 transitive deps
  restored quietly into `obj/`).
- `local.settings.json` extended with `AzureSignalRConnectionString`
  placeholder and `Host.CORS: "*"`.
- Security hardening: `.claude/settings.json` deny rules block Claude tools
  from reading, editing, or writing any `**/local.settings.json` (see
  Decisions).
- **SignalR primary key rotated** via Azure portal 2026-05-29. The
  previously leaked key is now invalid. User removed the new key from
  `local.settings.json` at end of session — it will need to be re-pasted
  (by the user, locally) when SP1-02 reaches the local test step in the
  next session.

Pending (in order):

1. Write `NegotiateFunction.cs` — POST `/api/negotiate`, returns
   `SignalRConnectionInfo` via `[SignalRConnectionInfoInput]` for hub
   `spike`.
2. Write `SpikeFunction.cs` — POST `/api/spike`, `[SignalROutput]` binding
   broadcasts a `{"text":"hello"}` message on target `"newMessage"` to
   the `spike` hub.
3. Write minimal `spike.html` — uses `@microsoft/signalr` from CDN,
   connects, prints incoming messages to a `<pre>` element.
4. Local end-to-end test: user pastes the new SignalR connection string
   into `local.settings.json`, runs `func start`, opens `spike.html`,
   curls `/api/spike`, confirms message appears in browser.

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

End-of-day snapshot so the next Claude Code session can pick up cleanly.

### Where we are

Mid-SP1-02. Azure side complete and verified. SignalR primary key rotated;
the old leaked key is dead. Code half of SP1-02 not yet started.

### Uncommitted changes in the working tree

Run `git status` to confirm. Expected:

- modified: `CLAUDE.md` — session start protocol section added.
- modified: `functions/SydneyPulse.Functions/SydneyPulse.Functions.csproj`
  — SignalR worker extension NuGet added.
- new: `.claude/settings.json` — permission deny rules for
  `**/local.settings.json`.
- new: `docs/sprints/progress.md` — this file.

Gitignored, not in `git status`:
`functions/SydneyPulse.Functions/local.settings.json` contains only the
placeholder; no real key.

Pre-existing untracked, leave alone: `.agents/`, `BUNDLE-README.md`,
`skills-lock.json`.

### Resume sequence

1. **Follow the session start protocol** per `CLAUDE.md`. Read this file
   and `sprint-1.md`, glob `docs/**/*.md`. Briefly report sprint state
   (active sprint, last completed item, next pending item, blocking risks).

2. **Propose commit grouping** for the uncommitted meta/process changes:
   - Commit A: `docs: add sprint progress tracker and session start protocol`
     — `CLAUDE.md` + `docs/sprints/progress.md`.
   - Commit B: `chore(security): deny Claude tools on local.settings.json`
     — `.claude/settings.json`.
   - Hold the `.csproj` change for the end-of-SP1-02 bundle commit.

   Get user approval before executing.

3. **Continue SP1-02 code work** — one file per turn, announce before
   editing:
   - Create `functions/SydneyPulse.Functions/Functions/NegotiateFunction.cs`.
     Hub `spike`. HTTP POST `/api/negotiate`. Returns
     `SignalRConnectionInfo` via `[SignalRConnectionInfoInput]`.
   - Create `functions/SydneyPulse.Functions/Functions/SpikeFunction.cs`.
     HTTP POST `/api/spike`. `[SignalROutput(HubName="spike")]` returns a
     `SignalRMessageAction` with target `"newMessage"` and arg
     `{"text":"hello"}`.
   - Create `spike.html` (project root or `spike/` subfolder). Uses
     `@microsoft/signalr` from CDN. POSTs to `/api/negotiate`, opens a
     `HubConnection`, registers `on("newMessage", ...)`, prints to
     `<pre id="log">`.
   - Build verify: `dotnet build functions/SydneyPulse.sln` → 0 errors,
     0 warnings.

4. **Local end-to-end test** (user actions — Claude does not run these):
   - User pastes the new SignalR connection string into
     `local.settings.json` (over the placeholder). Claude is denied from
     reading this file by `.claude/settings.json` — do not attempt Read,
     Edit, or Write on it.
   - User runs `cd functions/SydneyPulse.Functions; func start`.
   - User opens `spike.html` in a browser, expects "connected" status.
   - User in another terminal:
     `curl -X POST http://localhost:7071/api/spike`.
   - **Success criterion:** browser receives the hello message within
     ~1 second. SignalR works end-to-end → SP1-02 risk gate cleared.

5. **Close SP1-02:**
   - Commit `feat(spike): SignalR end-to-end de-risk spike` covering
     `.csproj`, `NegotiateFunction.cs`, `SpikeFunction.cs`, `spike.html`,
     plus any README spike-section addition.
   - Flip SP1-02 row to ✅ in the backlog table above; fill Done date and
     commit hashes; convert the "Done" prose into a closed-item summary.

### Standing operating rules for the next session

- The user runs all Azure mutations and all `func start` / test commands
  themselves. Claude provides instructions and read-only verification.
- One file change per turn. Announce the file and why before editing.
- The `.claude/settings.json` deny rule blocks Claude from Read / Edit /
  Write on `**/local.settings.json`. Do not attempt those tool calls;
  they will fail.
- User is on Windows / PowerShell. Provide single-line commands when
  asking the user to paste; backtick line continuation breaks under paste.
- TfNSW API key is held by the user; goes into Key Vault during SP1-03
  (Bicep), never into `local.settings.json`.
