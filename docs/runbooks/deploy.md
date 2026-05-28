# Runbook: deploy

How to deploy Sydney Pulse to dev or prod environments.

## Routine deploys

### Dev environment (auto)

Pushing to `main` triggers `.github/workflows/deploy.yml` which deploys
to dev automatically. No manual intervention needed.

To watch a deploy in progress:

```bash
gh run watch
```

To check the latest dev deployment status:

```bash
gh run list --workflow=deploy.yml --limit 5
```

### Prod environment (manual approval)

Prod deploys are triggered manually after dev verification:

```bash
gh workflow run deploy.yml -f environment=prod
```

The workflow will pause at the `prod-approval` environment in GitHub
Actions. Approve via the GitHub UI (Settings → Environments →
sydney-pulse-prod → review deployments).

After approval, the workflow:

1. Deploys Bicep to the prod resource group
2. Builds and publishes the Function App package to the **staging slot**
3. Runs smoke tests against the staging slot URL
4. If smoke tests pass: swaps staging with production
5. Runs post-swap verification against the prod URL
6. Tags the GitHub release with the build version

If any step fails after the slot swap, see the rollback runbook.

## Pre-deploy checklist

Before triggering a prod deploy:

- [ ] All dev tests passing (CI is green)
- [ ] Dev environment has been tested manually for the change
- [ ] No active incidents (check `/ops` SLO dashboard)
- [ ] Cost budget within $20/month limit
- [ ] If touching messaging or storage, `what-if` output reviewed
      for unintended changes
- [ ] PR description includes ADR reference if a design decision changed

## Bicep deploys without app code changes

For infra-only changes (Bicep modifications with no Function or Angular
changes), use:

```bash
gh workflow run deploy-infra.yml -f environment=prod
```

This skips the slot swap step (no new app code to deploy) and applies
Bicep directly.

## Validating after a deploy

After any prod deploy, manually verify:

1. Live URL responds: `curl -I https://sydney-pulse-swa-prod.azurestaticapps.net`
2. API health check: `curl https://sydney-pulse-func-prod.azurewebsites.net/api/health`
3. Live dashboard shows moving vehicles (open in browser)
4. App Insights live metrics shows incoming requests
5. No new alerts firing in Azure Monitor

Expected timeline:

- Bicep deployment: 3–6 minutes
- Function publish to staging slot: 1–2 minutes
- Smoke tests: 1 minute
- Slot swap: 30 seconds
- Total: 6–10 minutes for a full prod deploy

## Tailing logs

App Insights live stream (recommended for active debugging):

```
Azure Portal → sydney-pulse-ai-prod → Live metrics
```

Recent traces from CLI:

```bash
az monitor app-insights query \
  --apps sydney-pulse-ai-prod \
  --analytics-query "traces | order by timestamp desc | take 100"
```

Function-specific logs:

```bash
az functionapp log tail \
  --name sydney-pulse-func-prod \
  --resource-group sydney-pulse-rg-prod
```

## Common deploy failures

| Failure | Likely cause | Fix |
|---|---|---|
| Bicep what-if shows changes to Service Bus namespace | Missing `existing` keyword | Add `existing` to namespace resource declaration |
| Smoke test fails on slot URL | Cold start exceeding 30s timeout | Increase smoke test timeout to 60s |
| Slot swap returns 503 briefly | Expected during swap (15–30s window) | Wait; verify after 1 min |
| App Insights ingestion exceeded | Daily cap hit, no new telemetry | Increase cap or wait until UTC midnight reset |
| Cosmos throttles on first request after swap | Cold connection pool | Acceptable; second request succeeds |

## Emergency change process

For urgent fixes that bypass the normal review:

1. Create a `hotfix/*` branch from `main`
2. Apply the fix
3. Open a PR with the `urgent` label
4. Self-approve (the urgent label allows this in branch protection)
5. Merge to trigger dev deploy
6. Verify in dev within 5 minutes
7. Run `gh workflow run deploy.yml -f environment=prod` and approve

Document the urgent change in the next standup or daily commit
summary. Hotfixes still get ADRs if they change an architectural
decision; ADRs can be written retroactively for emergency changes.
