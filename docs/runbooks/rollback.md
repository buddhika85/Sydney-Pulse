# Runbook: rollback

How to revert a bad deployment in prod.

## Decision tree

Three rollback paths depending on what's broken and how recent the
deployment is.

```
Did the deploy complete?
├── No, failing at smoke tests on staging slot
│   └── Do nothing. Staging slot is isolated; prod is unaffected.
│       Investigate the staging slot, fix, redeploy.
│
├── Yes, but prod is now broken
│   ├── < 1 hour since deploy → Slot re-swap (fastest)
│   ├── < 24 hours since deploy → Deploy previous Git SHA
│   └── > 24 hours → Roll-forward fix is usually safer
│
└── Yes, infra-only deploy is broken
    └── Bicep revert: redeploy previous parameter file values
```

## Path 1: Slot re-swap (fastest, ~30 seconds)

If a prod deploy completed but production is misbehaving and it happened
within the last hour, the previous version is still in the staging
slot (because the swap moved it there).

```bash
az functionapp deployment slot swap \
  --name sydney-pulse-func-prod \
  --resource-group sydney-pulse-rg-prod \
  --slot staging \
  --target-slot production
```

This swaps slots again, putting the previous version back into
production. Total downtime: 15–30 seconds.

After re-swap:

1. Verify the prod URL responds with the older version's behavior
2. Tag the rollback in GitHub Releases with `-rollback` suffix
3. Open an incident report in `/docs/incidents/` (create folder if missing)
4. Do NOT redeploy until the bug is understood and fixed

## Path 2: Redeploy a previous Git SHA (~10 minutes)

If more than an hour has passed (staging slot may have been overwritten
by another deploy) or you need to go back further:

```bash
# Find the SHA you want to deploy
git log --oneline main

# Trigger the deploy workflow against that SHA
gh workflow run deploy.yml \
  -f environment=prod \
  --ref <sha>
```

This is just a normal deploy with a different ref. Goes through staging
slot, smoke test, swap.

## Path 3: Bicep-only revert (infra changes)

If a Bicep change caused the issue and no app code has changed since:

```bash
# Check out the previous good commit
git checkout <previous-sha> -- infra/

# Deploy infra-only workflow
gh workflow run deploy-infra.yml -f environment=prod

# Revert the working tree change
git checkout main -- infra/
```

Bicep is declarative — applying the previous state restores the previous
configuration. Be aware: some resources don't support all property
changes (renaming, region changes, etc.). The `what-if` step will warn
you of these.

## What rollback cannot fix

Some things cannot be rolled back by slot swap:

- **Cosmos schema changes** — adding fields is non-breaking; removing
  fields will not auto-restore data. Migrations must be reversible by
  design.
- **Event Grid schema changes** — events already published with the
  new schema may be consumed by the rolled-back code, which won't
  understand them. Consider event filtering.
- **Storage layout changes in Data Lake** — files written with new
  partition layout stay where they were written. Old code reading the
  archive must handle both layouts during transition.
- **Service Bus message format** — in-flight messages on the topic may
  be in the new format when the rolled-back consumer expects the old
  one. Drain the topic before rolling back if format changed.

For these cases, roll **forward** with a fix rather than rolling back.

## Post-rollback checklist

After any rollback:

- [ ] Prod URL serving correctly (verify in browser)
- [ ] API health endpoint returns 200
- [ ] No active alerts firing in Azure Monitor
- [ ] Open a PR to revert the offending change in `main` branch
- [ ] Write a short incident note in `/docs/incidents/YYYY-MM-DD-summary.md`
- [ ] If the bug warrants it, add a regression test before the next deploy
- [ ] Update the relevant ADR if the rollback reveals a design flaw

## Communicating a rollback

For portfolio scale: there are no users to notify. But practice the
discipline anyway:

- LinkedIn update: optional for educational value if rollback was
  significant. Frame as "what I learned."
- GitHub release notes: tag the rollback release with `-rollback`
  suffix (e.g. `v0.3.0-rollback`)
- Commit message on the revert PR: explain *why* concisely.
  Conventional Commit format: `revert: <original message>` with
  body explaining the failure mode.

## Practice rollback

In dev, deliberately deploy a bad change once per sprint to practice
the rollback procedure. The first time you need it in prod should
not be the first time you've done it.
