# ADR-0004: Bicep over Terraform for infrastructure-as-code

| | |
|---|---|
| Status | Accepted |
| Date | 2026-05-28 |
| Deciders | Project author |

## Context

Sydney Pulse needs infrastructure-as-code for repeatable, reviewable
deployments across dev and prod environments. The project targets
Azure exclusively and is positioned as an AZ-400 portfolio piece.

Two mature options are available:

1. Bicep — Azure-native DSL that compiles to ARM templates.
2. Terraform — multi-cloud declarative IaC with the AzureRM provider.

## Decision

Use Bicep for all infrastructure declarations. Organize modules under
`/infra/modules/` with parameter files per environment under
`/infra/parameters/`.

## Consequences

Positive:

- Native Azure tooling. Same-day support for new Azure resources and
  features without waiting on a third-party provider release.
- Strong VS Code authoring experience with IntelliSense, resource
  validation, and inline what-if previews.
- AZ-400 exam objectives weight Bicep as the recommended IaC for the
  Microsoft platform. Demonstrating Bicep proficiency aligns directly
  with the portfolio's target audience.
- `az deployment group what-if` is excellent for PR reviews — it shows
  exact resource diffs without applying.
- No remote state to manage (Azure stores deployment history on the
  resource group natively). One less moving part than Terraform.

Negative:

- Single-cloud lock-in. If Sydney Pulse ever needed to run on AWS or
  GCP, the entire infra layer would be rewritten. Acceptable — the
  project is explicitly an Azure showcase.
- Smaller community than Terraform. Slightly fewer examples and
  Stack Overflow answers. Mitigated by Microsoft's own Bicep samples
  repo being comprehensive.
- Loop and conditional syntax in Bicep is less expressive than
  Terraform's HCL. Not a blocker for Sydney Pulse's scope.

## Alternatives considered

**Terraform with AzureRM provider.** Rejected for the AZ-400 alignment
reason above. A side-benefit of Bicep here is that it doesn't require a
state file backend (Azure Storage) which would add ~$1/month of cost and
one more thing to secure.

**ARM templates (raw JSON).** Rejected as too verbose. Bicep compiles to
ARM and offers a strict superset of the capabilities at a fraction of
the line count.

**Pulumi (TypeScript-based IaC).** Rejected because adding a third
language alongside C# and TypeScript without clear benefit increases
cognitive load.

## Future option

If Sprint 4 has spare time, a parallel Terraform implementation of one
small module (for example, the storage account) on a separate branch
would be a portfolio talking point: *"I can articulate the tradeoffs
between Bicep and Terraform with code in both."* This is an aspiration,
not a commitment.

## Related decisions

- ADR-0006 — Deployment slots are declared in `infra/modules/compute.bicep`
- ADR-0008 — SignalR Free SKU declared via parameter, easy to override
  in prod params if upgrading.
