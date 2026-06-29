# GitHub Actions workflows

CI/CD for Sydney Pulse — automated by SP1-12, replaces the manual
`docs/runbooks/manual-deploy-dev.md` recipe.

## File layout

```
.github/workflows/
├── ci.yml              # PR trigger — lint + test + bicep what-if
├── deploy-dev.yml      # push-to-main + dispatch — full deploy pipeline
├── README.md           # this file
└── reusable/
    ├── dotnet-lint-test.yml   # format check + build + test + bicep validation
    ├── bicep-deploy.yml       # OIDC login + az deployment group create
    └── func-publish.yml       # build, zip, az zip-deploy + smoke check
```

## What each file does

### `ci.yml` — PR merge gate

- **Trigger:** `pull_request` to `main`
- **Calls:** `reusable/dotnet-lint-test.yml` with `runWhatIf=true`
- **Purpose:** never merge a red build; surface infra drift in PR review
- **Checks:** dotnet format, build, test, bicep build, bicep what-if
- **Mutations:** none — pure validation

### `deploy-dev.yml` — full pipeline

- **Trigger:** `push` to `main` (auto) + `workflow_dispatch` (manual re-run)
- **Concurrency:** `deploy-dev` group, one deploy at a time
- **Jobs (sequential):** `lint-test` → `deploy-infra` → `publish-app`
- **Purpose:** every merge to `main` deploys to `sydney-pulse-rg-dev`

### `reusable/dotnet-lint-test.yml`

- `workflow_call` — invoked by `ci.yml` + `deploy-dev.yml`
- **Steps:** `dotnet format --verify-no-changes` → restore → build → test → `az bicep build`
- **Optional:** when `runWhatIf=true`, adds OIDC login + `az deployment group what-if`
- **Mutations:** none (Bicep what-if is read-only)

### `reusable/bicep-deploy.yml`

- `workflow_call` — invoked by `deploy-dev.yml` (and future `deploy-prod.yml`)
- **Inputs:** `environment`, `resourceGroup`, `bicepParams`, `bicepEntrypoint`
- **Steps:** OIDC `azure/login` → `az deployment group create`
- **Idempotent** — Bicep is declarative, re-running is a no-op

### `reusable/func-publish.yml`

- `workflow_call` — invoked by `deploy-dev.yml`
- **Inputs:** `environment`, `functionAppName`, `resourceGroup`, `functionsProject`
- **Steps:** `dotnet publish` → zip → OIDC login → `az functionapp deployment source config-zip` → smoke check (state == Running)
- **Failure modes:** build error, deploy error, function app not Running post-deploy

## How GitHub authenticates to Azure (OIDC, short version)

These workflows never store an Azure client secret in the repo. They use
**OpenID Connect (OIDC) federation** — a passwordless trust pattern.

### What OIDC is

- **OpenID Connect** is a standard auth protocol built on OAuth 2.0
- Identity providers (GitHub Actions) issue **short-lived signed JWTs**
  about the workload that wants access
- Relying parties (Azure AD) **trust the issuer** and exchange the JWT
  for their own access token — no shared password ever
- The JWT carries claims that scope *who* the request is from:
  `iss` (issuer), `sub` (subject — e.g. repo + branch + environment),
  `aud` (audience)

### How the GitHub ↔ Azure handshake works at run time

```
Runner          GitHub OIDC provider          Azure AD              Azure ARM
  │             (token.actions.                  │                      │
  │              githubusercontent.com)          │                      │
  │                       │                      │                      │
  │ 1. Request OIDC token │                      │                      │
  │    (id-token: write)  │                      │                      │
  ├──────────────────────►│                      │                      │
  │                       │                      │                      │
  │ 2. Signed JWT         │                      │                      │
  │◄──────────────────────┤                      │                      │
  │    iss = token.actions.githubusercontent.com │                      │
  │    sub = repo:OWNER/REPO:environment:dev     │                      │
  │    aud = api://AzureADTokenExchange          │                      │
  │                                              │                      │
  │ 3. azure/login@v2 trades JWT for Azure       │                      │
  │    access token                              │                      │
  ├─────────────────────────────────────────────►│                      │
  │                                              │ validates JWT        │
  │                                              │ signature + checks   │
  │                                              │ federated credential │
  │                                              │ subject claim        │
  │                                              │                      │
  │ 4. Azure access token                        │                      │
  │◄─────────────────────────────────────────────┤                      │
  │    aud = https://management.azure.com/       │                      │
  │                                              │                      │
  │ 5. az deployment group create                │                      │
  ├─────────────────────────────────────────────────────────────────────►│
  │    Bearer <access token>                     │                      │
  │                                              │                      │
```

**Two tokens, two trust domains:**

- Steps 1–2: **identity assertion** — GitHub OIDC provider mints a JWT
  proving "this workflow is running in this repo / branch / environment"
- Steps 3–4: **token exchange** — Azure AD validates the JWT against the
  federated credential's subject claim, then issues its own access token
- Step 5: **authorisation** — the Azure access token is the bearer token
  ARM accepts to perform the deploy

### One-time trust setup (per environment)

In Azure AD, an **App Registration** is created with a **federated
credential** that says:

> "Trust GitHub's OIDC tokens whose `iss` is
> `token.actions.githubusercontent.com` AND `sub` is
> `repo:gsoft85512/Sydney-Pulse:environment:dev`."

Three federated credentials are added — one per trigger pattern
(branch push, pull request, environment dispatch). Each scoping is
exact: a wrong branch name or environment name fails token exchange
with `AADSTS70021`.

The App Registration is granted RBAC (`Contributor` +
`User Access Administrator`) on `sydney-pulse-rg-dev` — the federated
identity inherits these when it logs in.

### Why this beats client secrets

- **No password to rotate** — JWTs are minted per workflow run, expire
  in minutes
- **No password to leak** — three repo "secrets" are just IDs
  (`AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`), all
  non-sensitive
- **Scoped per repo + branch + environment** — a compromised workflow
  in one repo can't get tokens for another
- **Auditable** — every token exchange logs `sub` and `iss` to Azure
  AD sign-in logs

### Full setup recipe

Step-by-step Azure bootstrap → `docs/runbooks/github-actions-oidc-setup.md`.

## Azure setup (one-time)

Required repo secrets (all non-sensitive IDs, no client secret):

- `AZURE_CLIENT_ID` — app registration client ID
- `AZURE_TENANT_ID` — DevPulse tenant ID
- `AZURE_SUBSCRIPTION_ID` — DevPulse subscription ID

Required GitHub environment: `dev` (Settings → Environments → New).

## Branch protection (recommended)

GitHub repo → Settings → Branches → main rule:

- Require pull request before merging
- Require status checks to pass: `lint-test / lint-test`
- Require linear history (matches squash-merge convention in CLAUDE.md)

## Adding prod (Sprint 2)

Add `deploy-prod.yml` mirroring `deploy-dev.yml` with:

- `resourceGroup: sydney-pulse-rg-prod`
- `bicepParams: infra/parameters/prod.bicepparam`
- `functionAppName: sydney-pulse-func-prod`
- `environment: prod` (GitHub environment with required reviewer)
- Slot swap step after `publish-app` (ADR-0006)
