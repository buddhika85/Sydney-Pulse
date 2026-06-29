# Runbook: GitHub Actions OIDC setup

One-time setup to wire GitHub Actions → Azure auth via workload identity
federation. Required before the SP1-12 CI/CD workflows can deploy.

## Why OIDC (not a service principal secret)

- **Passwordless** — no client secret to rotate or leak
- **Scoped per repo + branch + environment** — the federated subject claim is exact
- **Senior-grade pattern** — AZ-400 documented topic
- **Three non-sensitive IDs only** as repo secrets — no credential material

## One-time setup

### 1. Create the app registration

```powershell
az ad app create --display-name sp-github-actions-dev
```

Capture the `appId` from the output — this is `AZURE_CLIENT_ID`.

Create the corresponding service principal:

```powershell
az ad sp create --id <appId>
```

### 2. Add federated credentials

Three federated credentials, one per trigger pattern.

**Replace `buddhika85/Sydney-Pulse` with the actual repo path if different.**

```powershell
# Push to main → deploy-dev.yml
az ad app federated-credential create --id <appId> --parameters '{
  "name": "sp-github-main-push",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:buddhika85/Sydney-Pulse:ref:refs/heads/main",
  "audiences": ["api://AzureADTokenExchange"]
}'

# PR → ci.yml what-if step
az ad app federated-credential create --id <appId> --parameters '{
  "name": "sp-github-pull-request",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:buddhika85/Sydney-Pulse:pull_request",
  "audiences": ["api://AzureADTokenExchange"]
}'

# workflow_dispatch with environment=dev → deploy-dev.yml manual re-run
az ad app federated-credential create --id <appId> --parameters '{
  "name": "sp-github-environment-dev",
  "issuer": "https://token.actions.githubusercontent.com",
  "subject": "repo:buddhika85/Sydney-Pulse:environment:dev",
  "audiences": ["api://AzureADTokenExchange"]
}'
```

### 3. Assign RBAC roles

The service principal needs:

- **Contributor** on `sydney-pulse-rg-dev` — for resource deploys
- **User Access Administrator** on `sydney-pulse-rg-dev` — for the RBAC assignments inside Bicep (Cosmos data plane, Key Vault Secrets User)

```powershell
$spId = az ad sp list --filter "appId eq '<appId>'" --query '[0].id' -o tsv
$subId = az account show --query id -o tsv

az role assignment create `
  --assignee-object-id $spId `
  --assignee-principal-type ServicePrincipal `
  --role "Contributor" `
  --scope "/subscriptions/$subId/resourceGroups/sydney-pulse-rg-dev"

az role assignment create `
  --assignee-object-id $spId `
  --assignee-principal-type ServicePrincipal `
  --role "User Access Administrator" `
  --scope "/subscriptions/$subId/resourceGroups/sydney-pulse-rg-dev"
```

### 4. Add repo secrets

Capture IDs first:

```powershell
az account show --query '{tenant: tenantId, sub: id}'
```

Set the three secrets:

```powershell
gh secret set AZURE_CLIENT_ID --body "<appId>"
gh secret set AZURE_TENANT_ID --body "<tenantId>"
gh secret set AZURE_SUBSCRIPTION_ID --body "<subId>"
```

### 5. Create GitHub environment `dev`

Repo → **Settings** → **Environments** → **New environment** → name = `dev`.

No protection rules needed for dev. For prod (Sprint 2), add required reviewers + wait timer.

## Verification (read-only)

```powershell
# App registration exists
az ad app list --display-name sp-github-actions-dev --query '[].{name:displayName, appId:appId}'

# Three federated credentials in place
az ad app federated-credential list --id <appId> --query '[].{name:name, subject:subject}'

# Both RBAC roles assigned
az role assignment list --assignee <appId> `
  --scope "/subscriptions/<subId>/resourceGroups/sydney-pulse-rg-dev" `
  --query '[].{role:roleDefinitionName}'

# Repo secrets visible by name (values not displayed)
gh secret list

# Environment exists
gh api repos/buddhika85/Sydney-Pulse/environments/dev --jq '.name'
```

## Failure modes

| Symptom | Likely cause |
|---|---|
| `AADSTS70021: No matching federated identity record found` | Subject claim mismatch — branch renamed, environment not created, or wrong repo path in the federated credential |
| `AuthorizationFailed: does not have authorization to perform action` on Bicep deploy | Missing RBAC role on RG — re-check Contributor + User Access Administrator are both assigned |
| `azure/login` fails on PR from fork | OIDC subjects don't cover external forks — expected, blocks community PRs (not a concern for this portfolio repo) |
| First push-to-main works, dispatch fails | `environment:dev` federated credential missing — add step 2 third entry |

## Adding prod (Sprint 2)

Repeat steps 1–5 with:

- App reg name `sp-github-actions-prod`
- Federated subject `repo:buddhika85/Sydney-Pulse:environment:prod`
- RBAC scope `sydney-pulse-rg-prod`
- Separate `AZURE_CLIENT_ID_PROD` repo secret (or use GitHub environment secrets scoped to `prod`)
- GitHub environment `prod` with required reviewer
