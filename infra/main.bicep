// main.bicep
// ----------
// Top-level deployment entry point for Sydney Pulse.
// Orchestrates all modules and wires their outputs together.
//
// Deploy to dev:
//   az deployment group create \
//     --resource-group sydney-pulse-rg-dev \
//     --template-file infra/main.bicep \
//     --parameters infra/parameters/dev.bicepparam
//
// Always run what-if first:
//   az deployment group what-if \
//     --resource-group sydney-pulse-rg-dev \
//     --template-file infra/main.bicep \
//     --parameters infra/parameters/dev.bicepparam

targetScope = 'resourceGroup'

// ── Parameters ───────────────────────────────────────────────────────────────

@description('Deployment environment.')
@allowed(['dev', 'prod'])
param environment string

@description('Azure region for all new resources.')
param location string = 'australiaeast'

@description('Function App hosting plan SKU. Y1 = Consumption plan.')
param functionAppSku string = 'Y1'

@description('SignalR Service SKU.')
param signalRSku string = 'Free_F1'

// Static Web Apps are not available in australiaeast. eastasia is the closest
// supported region; content is still served globally via CDN.
@description('Region for the Static Web App management plane.')
param swaLocation string = 'eastasia'

// Pre-existing Service Bus namespace — never modified, only referenced (ADR-0003).
@description('Name of the pre-existing Service Bus Standard namespace.')
param existingServiceBusNamespaceName string

@description('Resource group containing the pre-existing Service Bus namespace.')
param existingServiceBusResourceGroup string

// ── Naming convention ─────────────────────────────────────────────────────────
// Hyphenated names follow sydney-pulse-<service>-<env>.
// Storage accounts require alphanumeric-only names (Azure constraint) so they
// use a condensed prefix: sydpulse<service><env>.

var prefix = 'sydney-pulse'
var names = {
  funcApp:      '${prefix}-func-${environment}'
  // Storage account names: no hyphens, max 24 chars.
  funcStorage:  'sydpulsestor${environment}'
  dataLake:     'sydpulsedlsa${environment}'
  cosmos:       '${prefix}-cosmos-${environment}'
  eventGrid:    '${prefix}-eg-${environment}'
  keyVault:     '${prefix}-kv-${environment}'
  appInsights:  '${prefix}-ai-${environment}'
  logAnalytics: '${prefix}-law-${environment}'
  signalR:      '${prefix}-signalr-${environment}'
  swa:          '${prefix}-swa-${environment}'
}

// Standard tags applied to every resource.
var tags = {
  project:     'sydney-pulse'
  environment: environment
  managedBy:   'bicep'
  costCenter:  'portfolio'
}

// ── Modules ───────────────────────────────────────────────────────────────────

// Key Vault — must exist before the Function App starts resolving
// @Microsoft.KeyVault(...) references in its app settings.
module security 'modules/security.bicep' = {
  name: 'security'
  params: {
    location:     location
    keyVaultName: names.keyVault
    tags:         tags
  }
}

// Log Analytics workspace + Application Insights.
module observability 'modules/observability.bicep' = {
  name: 'observability'
  params: {
    location:         location
    logAnalyticsName: names.logAnalytics
    appInsightsName:  names.appInsights
    tags:             tags
  }
}

// Cosmos DB (serverless) + storage accounts.
module data 'modules/data.bicep' = {
  name: 'data'
  params: {
    location:        location
    cosmosName:      names.cosmos
    funcStorageName: names.funcStorage
    dataLakeName:    names.dataLake
    tags:            tags
  }
}

// Service Bus topic on the shared namespace — deployed to the SB resource
// group via scope. Must complete before messaging so the alerter subscription
// destination resource ID resolves correctly at deploy time.
module serviceBusTopic 'modules/servicebus-topic.bicep' = {
  name: 'serviceBusTopic'
  scope: resourceGroup(existingServiceBusResourceGroup)
  params: {
    existingServiceBusNamespaceName: existingServiceBusNamespaceName
  }
}

// Event Grid custom topic + subscriptions (including the alerter subscription
// whose destination is the SB topic created above).
module messaging 'modules/messaging.bicep' = {
  name: 'messaging'
  params: {
    location:                        location
    eventGridTopicName:              names.eventGrid
    existingServiceBusNamespaceName: existingServiceBusNamespaceName
    existingServiceBusResourceGroup: existingServiceBusResourceGroup
    tags:                            tags
  }
  // Alerter subscription destination references the SB topic by computed
  // resource ID — ensure the topic exists first.
  dependsOn: [serviceBusTopic]
}

// SignalR Service (Free SKU per ADR-0008) + Static Web App.
module frontend 'modules/frontend.bicep' = {
  name: 'frontend'
  params: {
    location:    location
    signalRName: names.signalR
    signalRSku:  signalRSku
    swaName:     names.swa
    swaLocation: swaLocation
    tags:        tags
  }
}

// Function App — depends on security (KV must exist) and observability
// (App Insights connection string needed at deploy time).
module compute 'modules/compute.bicep' = {
  name: 'compute'
  params: {
    location:               location
    funcAppName:            names.funcApp
    funcStorageAccountName: names.funcStorage
    functionAppSku:         functionAppSku
    keyVaultName:           names.keyVault
    appInsightsConnStr:     observability.outputs.appInsightsConnectionString
    cosmosEndpoint:         data.outputs.cosmosEndpoint
    eventGridTopicEndpoint: messaging.outputs.eventGridTopicEndpoint
    tags:                   tags
  }
  dependsOn: [security]
}

// Role assignments — declared after compute so principalId is available.
module roleAssignments 'modules/role-assignments.bicep' = {
  name: 'roleAssignments'
  params: {
    functionPrincipalId:  compute.outputs.principalId
    keyVaultName:         names.keyVault
    cosmosAccountName:    names.cosmos
    eventGridTopicName:   names.eventGrid
    dataLakeStorageName:  names.dataLake
    funcStorageName:      names.funcStorage
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output functionAppName string = names.funcApp
output keyVaultUri string = security.outputs.keyVaultUri
output cosmosEndpoint string = data.outputs.cosmosEndpoint
output staticWebAppHostname string = frontend.outputs.swaDefaultHostname
output appInsightsConnectionString string = observability.outputs.appInsightsConnectionString
