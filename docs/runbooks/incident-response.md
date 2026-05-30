# Runbook: incident response

How to respond to an active incident in Sydney Pulse.

## What counts as an incident

For this portfolio project, an incident is anything that:

- Makes the live URL unavailable
- Causes the dashboard to display incorrect data persistently (> 5 min)
- Burns through the daily TfNSW API quota unexpectedly
- Generates Azure cost above $1 per hour sustained
- Fires an Azure Monitor alert rule

For a real production system the criteria would be tighter and tied
to user impact and SLOs. The same response framework applies.

## First five minutes

1. **Acknowledge**. If an Azure Monitor alert fired, acknowledge it in
   the portal so it stops re-sending notifications.

2. **Check the `/ops` screen**. It shows current SLO status, recent
   traces, and recent deployments. The cause is usually visible there.

3. **Check recent deployments**. If a deploy happened in the last
   hour, the rollback runbook is probably the right answer. Don't
   start debugging before considering whether to roll back first.

4. **Capture state**. Take screenshots of the `/ops` screen, run the
   following query in App Insights and save the result:

```kusto
union requests, dependencies, exceptions, traces
| where timestamp > ago(15m)
| order by timestamp desc
| take 200
```

5. **Decide**: roll back, or roll forward with a fix? Default to
   rollback if a recent deploy is plausibly the cause.

## Common incident patterns

Refer to [docs/diagrams.md](../diagrams.md) to trace the affected data path through the topology before diving into a specific pattern below.

### "Live dashboard shows no vehicles"

Possible causes, in order of likelihood:

1. TfNSW API outage — check https://opendata.transport.nsw.gov.au/
2. Poller Function not running — check timer trigger schedule
3. Event Grid subscription disabled — check Azure Portal
4. Cosmos DB throttled — check 429 metric on Cosmos
5. State Writer Function failing — check Function Apps invocations

Mitigation: switch prod to `demo` mode (see `/docs/modes.md`) while
investigating. Users see a recorded snapshot instead of an empty map.

### "Alerts not showing in real time"

Possible causes:

1. Service Bus DLQ filling — check DLQ depth metric
2. SignalR Service hit message cap (20k/day) — check usage metric
3. Alerter Function failing — check exception telemetry
4. Subscription filter misconfigured — check that ServiceAlert events
   are matching the filter

Mitigation: the HTTP API `/api/alerts` returns the same data; the
frontend falls back to polling every 30s if SignalR connection drops.
This is graceful degradation.

### "Cosmos costs spiking"

Possible causes:

1. Hot loop in State Writer (retry without backoff)
2. Frontend polling the API too aggressively
3. Application Insights ingestion misconfigured to write to Cosmos

Mitigation: temporarily scale Function App to "stopped" state to halt
writes. Investigate logs. Resume when fixed.

### "TfNSW quota exhausted"

Quota is 60,000 requests/day. The Poller at 30-second intervals does
14,400/day for the 5 modes — well under quota. If quota is exceeded:

1. Check if `TfNswFeedClient` retry policy is in a loop (Polly
   misconfiguration)
2. Check if a developer is hitting the API from local with the prod
   key
3. Check if a second Function instance is duplicating polls
   (`Singleton` attribute should prevent this)

Mitigation: rotate the API key (forces all callers to re-auth from
Key Vault), or wait until UTC midnight for quota reset.

## Communication

For portfolio scale:

- Self-document in `/docs/incidents/YYYY-MM-DD-shortname.md`
- If recorded during an active job search, the incident report is
  itself portfolio gold — turn it into a blog post

For a real system, this section would cover:

- Stakeholder notifications
- Status page updates
- Customer support coordination
- Post-incident reviews with the team

## Post-incident

Within 48 hours of resolution, write an incident report:

```markdown
# Incident YYYY-MM-DD: Short title

## Summary
One paragraph: what happened, impact, duration, resolution.

## Timeline (Sydney time)
- HH:MM — Alert fired (or first symptom)
- HH:MM — Acknowledged, investigation began
- HH:MM — Root cause identified
- HH:MM — Mitigation applied
- HH:MM — Resolution confirmed

## Root cause
Specific technical cause. Not "human error" — what made the error
possible or undetected.

## Impact
What was broken, for how long, who saw it.

## What went well
- Detection time, monitoring catching it, etc.

## What went poorly
- Things that delayed detection, mitigation, or resolution.

## Action items
- [ ] Specific work to prevent recurrence (owner, due date)
- [ ] Monitoring improvement
- [ ] Documentation update
```

Store these in `/docs/incidents/`. Reference from related ADRs.

## Practice

Once per sprint, run a "game day" exercise:

- Intentionally break something in dev (delete a topic subscription,
  block the Function App, set Cosmos to 0 RU/s)
- Walk through the response runbook from the top
- Time how long detection-to-mitigation takes
- Update this runbook with any gaps discovered

The goal isn't to never have incidents. The goal is to respond
quickly when they happen and to learn from them.
