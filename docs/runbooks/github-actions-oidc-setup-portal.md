# Runbook: GitHub Actions OIDC setup (Portal + GitHub UI)

GUI companion to [`github-actions-oidc-setup.md`](github-actions-oidc-setup.md)
which uses the `az` + `gh` CLIs. Same outcome; this file walks Azure Portal +
GitHub UI clicks for users who prefer visual flows or don't have both CLIs
configured.

One-time setup to wire GitHub Actions → Azure auth via workload identity
federation. Required before the SP1-12 CI/CD workflows can deploy.

## Why OIDC (not a service principal secret)

- **Passwordless** — no client secret to rotate or leak
- **Scoped per repo + branch + environment** — the federated subject claim is exact
- **Senior-grade pattern** — AZ-400 documented topic
- **Three non-sensitive IDs only** as repo secrets — no credential material

## Prerequisites

- You can sign in to the Azure Portal as a user with `Owner` or
  `User Access Administrator` on `sydney-pulse-rg-dev` (needed to assign
  roles to the new service principal).
- You can sign in to GitHub as a repo admin on `buddhika85/Sydney-Pulse`
  (needed to add repo secrets + create environments).
- You know the tenant ID and subscription ID — both are visible in the
  Portal once signed in.

## Capture these values up front

Keep a scratch pad open. You'll paste these into GitHub secrets in Step 4.

| Value | Where to find it in the Portal |
|---|---|
| **Tenant ID** | Microsoft Entra ID → **Overview** → **Tenant ID** (copy icon) |
| **Subscription ID** | Subscriptions → DevPulse Subscription → **Subscription ID** on the Overview blade |
| **Application (client) ID** | Captured in Step 1 after creating the app reg |

## One-time setup

### Step 1 — Create the app registration

**Portal:** Microsoft Entra ID → **App registrations** → **+ New registration**

- **Name:** `sp-github-actions-dev`
- **Supported account types:** Accounts in this organizational directory only (single tenant)
- **Redirect URI:** leave blank — OIDC federation does not use a redirect URI
- Click **Register**

On the new app registration's **Overview** page, copy two values:

- **Application (client) ID** — this is `AZURE_CLIENT_ID` for Step 4
- **Directory (tenant) ID** — confirm it matches the Tenant ID from your scratch pad

> **Why no separate service principal step?** The Portal auto-creates the
> service principal in the home tenant when you register the app. The CLI
> equivalent (`az ad sp create --id <appId>`) is not needed here.

### Step 2 — Add federated credentials (three entries)

**Portal:** Your new app registration → **Certificates & secrets** (left
nav) → **Federated credentials** tab → **+ Add credential**

Repeat the form three times — one per trigger pattern. Common fields for
all three:

- **Federated credential scenario:** `GitHub Actions deploying Azure resources`
- **Organization:** `buddhika85`
- **Repository:** `Sydney-Pulse`
- **Audience:** `api://AzureADTokenExchange` (pre-filled, leave as-is)

Per-entry fields:

| # | Purpose | Entity type | Value | Name |
|---|---|---|---|---|
| 2a | Push to main → `deploy-dev.yml` | **Branch** | `main` | `sp-github-main-push` |
| 2b | PR → `ci.yml` what-if step | **Pull request** | _(none — entity type alone is enough)_ | `sp-github-pull-request` |
| 2c | `workflow_dispatch` env=dev → manual re-run | **Environment** | `dev` | `sp-github-environment-dev` |

> **Tip:** The Portal previews the generated `subject` claim near the
> bottom of the form before you click **Add**. It should match:
>
> - 2a → `repo:buddhika85/Sydney-Pulse:ref:refs/heads/main`
> - 2b → `repo:buddhika85/Sydney-Pulse:pull_request`
> - 2c → `repo:buddhika85/Sydney-Pulse:environment:dev`
>
> If the subject doesn't match exactly, the GitHub-Actions → Azure-AD
> handshake fails at runtime with `AADSTS70021`. Verify the preview
> before clicking Add.

### Step 3 — Assign RBAC roles to the service principal

The service principal needs two roles on `sydney-pulse-rg-dev`:

- **Contributor** — for resource deploys (Bicep, Function App publish, etc.)
- **User Access Administrator** — because `infra/modules/role-assignments.bicep` writes role assignments (e.g. Cosmos Built-in Data Contributor, Key Vault Secrets User). Contributor alone cannot write to `Microsoft.Authorization/roleAssignments`.

**Portal:** Resource groups → **sydney-pulse-rg-dev** → **Access control (IAM)** (left nav) → **+ Add** → **Add role assignment**

Repeat the flow twice.

**Step 3a — Assign Contributor:**

1. **Role** tab → search **Contributor** → select it → **Next**
2. **Members** tab → **Assign access to:** User, group, or service principal → **+ Select members** → search `sp-github-actions-dev` → click it → **Select** → **Next**
3. **Conditions** tab → leave defaults → **Next**
4. **Review + assign** → **Review + assign**

**Step 3b — Assign User Access Administrator:**

1. **Role** tab → **Privileged administrator roles** sub-tab → select **User Access Administrator** → **Next**
2. **Members** tab → same flow as 3a, pick `sp-github-actions-dev` → **Next**
3. **Conditions** tab — Azure now requires a condition on User Access Administrator (added Aug 2024). Two options:
   - **Allow user to assign all roles** — simplest for dev. Pick this.
   - **Constrain roles** — narrower, for prod hardening. Constrain to just the roles the Bicep needs (`Key Vault Secrets User`, `Cosmos Built-in Data Contributor`, `EventGrid Data Sender`, `Storage Blob Data Contributor`, `Storage Blob Data Owner`, `Storage Queue Data Contributor`, `Storage Table Data Contributor`).
4. **Review + assign** → **Review + assign**

> **Why two role assignments and not just Owner?** Owner would work, but
> Owner on a resource group is broader than this SP needs (it adds
> ability to delete the RG, change resource locks, etc.). Contributor +
> User Access Administrator is the least-privilege pair that still lets
> Bicep do its full job. Senior pattern.

### Step 4 — Add repo secrets to GitHub

**GitHub:** Your repo → **Settings** → **Secrets and variables** → **Actions** → **Secrets** tab → **New repository secret**

Add three secrets:

| Name | Value |
|---|---|
| `AZURE_CLIENT_ID` | Application (client) ID from Step 1 |
| `AZURE_TENANT_ID` | Tenant ID from your scratch pad |
| `AZURE_SUBSCRIPTION_ID` | Subscription ID from your scratch pad |

> **None of these are sensitive on their own.** They're just IDs — knowing
> them does not let an attacker authenticate. The actual trust gate is the
> federated subject claim from Step 2, validated by Azure AD against the
> GitHub-issued JWT. This is the heart of why OIDC beats a client secret.

### Step 5 — Create GitHub environment `dev`

**GitHub:** Your repo → **Settings** → **Environments** → **New environment**

- **Name:** `dev`
- Click **Configure environment**
- No protection rules needed for dev. Leave defaults.
- (Optional) Add a deployment branch rule: `main` only — prevents accidental deploys from feature branches.

> **Why does an empty environment matter?** GitHub Actions only mints an
> OIDC JWT with `sub: ...:environment:dev` when the job declares
> `environment: dev`. If the environment doesn't exist, the JWT is
> issued with a different `sub` shape and the Step 2c federated
> credential doesn't match. The environment doesn't have to *do*
> anything — it just has to *exist*.
>
> **For prod (Sprint 2):** add required reviewers + wait timer here.

## Verification (Portal + GitHub, read-only)

Quick spot-checks after the setup is done. None of these mutate anything.

| What to verify | Where |
|---|---|
| App registration exists | Entra ID → **App registrations** → search `sp-github-actions-dev` |
| 3 federated credentials present | App reg → **Certificates & secrets** → **Federated credentials** tab → expect 3 rows with subjects matching Step 2's preview |
| Subject claims look correct | Click each row → confirm the `subject` field matches the table above |
| 2 role assignments on the RG | Resource group `sydney-pulse-rg-dev` → **Access control (IAM)** → **Role assignments** tab → search `sp-github-actions-dev` → expect 2 rows (Contributor + User Access Administrator) |
| 3 repo secrets present | GitHub repo → **Settings** → **Secrets and variables** → **Actions** → expect `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID` listed (values masked, as expected) |
| Environment exists | GitHub repo → **Settings** → **Environments** → expect `dev` row |

If all six checks pass, the OIDC setup is complete. Trigger a test run
from `workflow_dispatch` on `deploy-dev.yml` to confirm end-to-end auth.

## Failure modes

| Symptom | Likely cause |
|---|---|
| `AADSTS70021: No matching federated identity record found` | Subject claim mismatch — branch renamed, environment not created, wrong repo path, or one of the three federated credentials missing |
| `AuthorizationFailed: does not have authorization to perform action` on Bicep deploy | Missing RBAC role on RG — re-check Contributor + User Access Administrator are both assigned (Step 3 verification row) |
| `azure/login` fails on PR from fork | OIDC subjects don't cover external forks — expected, blocks community PRs (not a concern for this portfolio repo) |
| First push-to-main works, dispatch fails | Step 2c federated credential (environment=dev) missing or has a typo in the environment name |
| `RoleAssignmentScopeMismatch` during Bicep apply | User Access Administrator condition is too narrow — Step 3b condition needs to permit the roles in `infra/modules/role-assignments.bicep` |
| Federated credential preview doesn't show expected subject | Wrong entity type — Branch for `main` push, Pull request for PRs (no value), Environment for dispatch with env input |

## Adding prod (Sprint 2)

Repeat Steps 1–5 with the prod variants:

| Item | Dev value | Prod value |
|---|---|---|
| App registration name | `sp-github-actions-dev` | `sp-github-actions-prod` |
| Federated subject (the one that matters for prod deploys) | `:environment:dev` | `:environment:prod` |
| RBAC scope | `sydney-pulse-rg-dev` | `sydney-pulse-rg-prod` |
| Repo secret naming | `AZURE_CLIENT_ID` (default scope) | `AZURE_CLIENT_ID` as a **GitHub environment secret** scoped to `prod` (recommended), or `AZURE_CLIENT_ID_PROD` as a repo secret |
| GitHub environment protection | None | Required reviewer + wait timer |

> **Separate app reg, not a 4th federated credential** — keeping prod on
> its own app registration means a dev-side mistake (wrong subject,
> overly broad role, accidental delete) cannot reach prod. Independent
> blast radius.

## Cross-reference

- CLI version of this runbook: [`github-actions-oidc-setup.md`](github-actions-oidc-setup.md)
- Background on OIDC handshake (4 actors, 2 tokens): [`.github/workflows/README.md`](../../.github/workflows/README.md) §"How GitHub authenticates to Azure (OIDC, short version)"
- Workflows that depend on this setup being complete: `deploy-dev.yml`, `ci.yml` (when `runWhatIf: true`)
