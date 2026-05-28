# Infra (Bicep)

Context for Claude Code when working in `/infra/`. The root `CLAUDE.md`
covers project-wide rules; this file covers Bicep-specific patterns.

## Layout

```
main.bicep                     Top-level deployment entry point
modules/
  network.bicep                Optional VNet, not used at portfolio scale
  messaging.bicep              Event Grid topic + Service Bus topic reference
  compute.bicep                Function App, App Service Plan, slots
  data.bicep                   Cosmos DB Serverless, Data Lake Gen2
  observability.bicep          App Insights, Log Analytics, alert rules
  security.bicep               Key Vault, role assignments
  frontend.bicep               Static Web App, SignalR Service
parameters/
  dev.bicepparam               Dev environment values
  prod.bicepparam              Prod environment values
```

## Bicep conventions

- **kebab-case for module file names** (`messaging.bicep`)
- **camelCase for parameters** (`functionAppName`, `cosmosThroughput`)
- **PascalCase for types** if defined (`@discriminator('kind')` etc.)
- **Use `existing`** for resources we reference but don't manage
  (Service Bus namespace, see ADR-0003)
- **Output what the caller needs** — connection strings, resource IDs,
  endpoint URLs. Never hardcode references between modules.
- **No interpolation of secrets into outputs.** Outputs are returned
  to the caller and end up in deployment logs. Use Key Vault
  references at the consumer side.

## Resource naming

All resources follow `sydney-pulse-<service>-<env>`:

- `sydney-pulse-rg-dev` — resource group
- `sydney-pulse-func-prod` — Function App
- `sydney-pulse-cosmos-prod` — Cosmos DB account
- `sydney-pulse-kv-prod` — Key Vault
- `sydney-pulse-signalr-prod` — SignalR Service
- `sydney-pulse-ai-prod` — Application Insights
- `sydney-pulse-law-prod` — Log Analytics workspace
- `sydney-pulse-swa-prod` — Static Web App
- `sydney-pulse-dlsa-prod` — Data Lake Storage Account
- `sydney-pulse-storage-prod` — Functions storage account

Tags on every resource:

```bicep
tags: {
  project: 'sydney-pulse'
  environment: environment
  managedBy: 'bicep'
  costCenter: 'portfolio'
}
```

## Parameter files

`dev.bicepparam`:

```bicep
using '../main.bicep'

param environment = 'dev'
param location = 'australiaeast'
param functionAppSku = 'Y1'
param cosmosCapacityMode = 'Serverless'
param signalRSku = 'Free_F1'
param appInsightsSamplingPercent = 5
param appInsightsDailyCapGb = 1
param existingServiceBusNamespaceName = 'sb-shared-prod'
param existingServiceBusResourceGroup = 'rg-shared-messaging'
```

`prod.bicepparam` has the same shape with prod values.

## Existing resources

The Service Bus namespace lives in a different resource group. Reference
it via `existing`:

```bicep
resource sbNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: existingServiceBusNamespaceName
  scope: resourceGroup(existingServiceBusResourceGroup)
}

resource alertsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'sydney-pulse-alerts'
  properties: { /* ... */ }
}
```

The Bicep deployment runs against the Sydney Pulse resource group but
this resource is created in the shared one. The deploying principal
needs `Azure Service Bus Data Owner` on the shared namespace.

## Role assignments

Functions need:

- `Key Vault Secrets User` on the Sydney Pulse Key Vault
- `Cosmos DB Built-in Data Contributor` on the Cosmos account
- `EventGrid Data Sender` on the Event Grid topic
- `Storage Blob Data Contributor` on the Data Lake storage account
- `Azure Service Bus Data Sender` for Event Grid (to send to SB topic)
- `Azure Service Bus Data Receiver` for the Alerter (to consume from SB)

All declared in `security.bicep`. Use Managed Identity, never connection
strings.

## What-if usage

Always run `az deployment group what-if` in a PR before merging. The
GitHub Actions PR validation workflow does this automatically and
comments the result on the PR.

If `what-if` shows changes to resources NOT in `/infra/`, something is
wrong — investigate before merging. The most common cause is referencing
the shared Service Bus namespace incorrectly (a missing `existing`
keyword would create a new namespace, which would be a $10/month
mistake).

## Common tasks

- Add a new resource: create or extend the relevant module file,
  update `main.bicep` to include the module, update parameter files
  if needed, run `bicep build` to validate, run `what-if` against
  dev, deploy.
- Add a new Function App setting: update `compute.bicep`'s
  `appSettings` array. Sensitive values go through Key Vault references.
- Promote a dev change to prod: update `prod.bicepparam` if the
  parameter differs, otherwise no change needed — the same main.bicep
  generates both.

## Don't

- Don't put secrets in parameter files. Use Key Vault references.
- Don't create resources outside `main.bicep` — every deployable
  resource is referenced from there for reproducibility.
- Don't use `latest` for API versions. Pin to a specific version that
  has been tested (`@2022-10-01-preview` etc.).
- Don't modify the existing Service Bus namespace's properties. Add
  topics; never touch the namespace itself (see ADR-0003).
- Don't deploy Bicep directly from a developer machine to prod. All
  prod deployments go through GitHub Actions for auditability.
