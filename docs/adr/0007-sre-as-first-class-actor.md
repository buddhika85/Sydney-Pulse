# ADR-0007: SRE / operator is a first-class system actor

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

When modelling the system's use cases, the obvious primary actor is the
Commuter. A secondary persona (Transit Analyst) was added to justify the
historical-analytics dimension. A third question arose: should the SRE /
operator be treated as a primary actor with a dedicated screen, or
relegated to "ops tooling outside the product"?

This decision shapes the use case model, the route table of the Angular
app, and the data plane (does the API serve telemetry, or is telemetry
only visible in Azure Portal)?

## Decision

The SRE / operator is a first-class primary actor with their own
screen at `/ops`. Operational telemetry — SLO status, distributed
traces, deployment history — is surfaced via the same HTTP API that
powers the commuter dashboard, not via direct Azure Portal access.

The HTTP API has dedicated endpoints:

- `GET /api/ops/slos` — current SLO values from Application Insights
- `GET /api/ops/recent-traces` — last 5 traces with span breakdown
- `GET /api/ops/deployments` — last 5 deployments from GitHub Actions API

## Consequences

Positive:

- Operability becomes a product surface. Stakeholders (in interviews,
  in stakeholder reviews) can *see* the SLOs without an Azure Portal
  login. This is a senior-engineer talking point that distinguishes
  the portfolio.
- Forces clear thinking about what operability means. The act of
  designing the `/ops` screen surfaced the SLO definitions (in
  `/docs/slos.md`) that would otherwise have been left implicit.
- The API endpoints for ops queries become a single integration
  surface that future tooling could consume (a Slack bot, a status
  page, a paging integration).

Negative:

- Three screens instead of two means more frontend work in Sprint 1
  and Sprint 3. The marginal cost is one Angular component and three
  API endpoints.
- API endpoints proxying KQL queries are slightly more complex than
  embedding an Application Insights workbook iframe. Worth it for
  the unified UI experience.

## Use cases enabled

Adding SRE as a primary actor means these use cases now exist in the
system:

- "Monitor system health" — read SLO dashboard
- "Investigate incidents" — trace a specific failed event end-to-end
- "Review deployment history" — see what shipped when, with what result
- "Receive SLO breach alert" — Azure Monitor alert rule sends to email

## Use cases explicitly NOT enabled

To bound scope:

- No incident response automation (no auto-rollback on SLO breach)
- No alert routing or escalation policies (single email destination)
- No paging integration (PagerDuty, Opsgenie) — overkill for portfolio
- No multi-tenant RBAC on the `/ops` screen — single operator persona

## Alternatives considered

**SRE accesses operations via Azure Portal only.** Rejected because it
removes operability from the product surface. Many portfolio projects
treat observability as an invisible afterthought; making it visible is
the point.

**Embed Application Insights workbook iframes in `/ops`.** Considered.
Provides richer visualizations but couples the UI to Azure Portal auth
and creates an awkward visual seam between custom UI and embedded
Microsoft chrome. Rejected for design consistency.

## Related decisions

- ADR-0005 — Angular routing has four routes, one per primary surface
  (landing + three actors)
- ADR-0010 — Operational alerts in scope of this actor
- `/docs/slos.md` — SLO definitions that the `/ops` screen visualizes

## Portfolio framing

When discussing this in interviews:
*"I treat the SRE as a first-class actor because the system isn't done
when it deploys. Operability is a use case the product must satisfy.
The /ops screen is built from the same API surface as the rest of the
app — there's no separate 'admin' system."*
