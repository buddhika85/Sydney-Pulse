// compute.bicep
// -------------
// Provisions the Azure Functions Consumption plan and Function App.
// System-assigned Managed Identity is enabled so the app can access
// Key Vault, Cosmos DB, Event Grid, and Storage without connection strings.
//
// Sensitive settings (TfNSW API key, SignalR connection string) are
// @Microsoft.KeyVault(...) references — resolved at runtime by Azure, never
// stored in plain text in app settings or deployment logs.
//
// AzureWebJobsStorage uses identity-based access (accountName only).
// Required storage roles on the func storage account (add in main.bicep):
//   - Storage Blob Data Owner
//   - Storage Queue Data Contributor
//   - Storage Table Data Contributor

@description('Azure region.')
param location string

@description('Function App resource name.')
param funcAppName string

@description('Storage account name for the Functions host (identity-based access).')
param funcStorageAccountName string

@description('Function App hosting plan SKU. Y1 = Consumption.')
param functionAppSku string

@description('Key Vault name — used to construct @Microsoft.KeyVault references.')
param keyVaultName string

@description('Application Insights connection string.')
param appInsightsConnStr string

@description('Cosmos DB account endpoint URL.')
param cosmosEndpoint string

@description('Event Grid custom topic endpoint URL.')
param eventGridTopicEndpoint string

@description('Data Lake Storage Gen2 account name for the Archiver (ADR-0012).')
param dataLakeStorageAccountName string

@description('Resource tags.')
param tags object

// ── Helper: Key Vault reference constructor ───────────────────────────────────
// Produces the @Microsoft.KeyVault(VaultName=...;SecretName=...) string.
// Azure resolves this at Function App startup via Managed Identity.
// vaultName must be passed explicitly — user-defined functions cannot close
// over outer parameters in Bicep.

func kvRef(vaultName string, secretName string) string =>
  '@Microsoft.KeyVault(VaultName=${vaultName};SecretName=${secretName})'

// ── Consumption plan ──────────────────────────────────────────────────────────

resource plan 'Microsoft.Web/serverfarms@2023-01-01' = {
  name: '${funcAppName}-plan'
  location: location
  tags: tags
  sku: {
    name: functionAppSku // Y1 = Consumption; scales to zero between polls.
    tier: 'Dynamic'
  }
  properties: {
    reserved: false // Windows host; isolated-worker .NET 8 runs on Windows.
  }
}

// ── Function App ──────────────────────────────────────────────────────────────

resource funcApp 'Microsoft.Web/sites@2023-01-01' = {
  name: funcAppName
  location: location
  tags: tags
  kind: 'functionapp'
  identity: {
    // System-assigned MI: Azure manages the credential lifecycle.
    type: 'SystemAssigned'
  }
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      // .NET 8 isolated-worker runtime.
      netFrameworkVersion: 'v8.0'
      use32BitWorkerProcess: false
      ftpsState: 'Disabled'
      minTlsVersion: '1.2'
      cors: {
        // SWA origin will be tightened per-environment after deployment.
        // '*' is acceptable during dev to unblock local spike.html testing.
        allowedOrigins: ['*']
      }
      appSettings: [
        // ── Runtime ──────────────────────────────────────────────────────────
        {
          name:  'FUNCTIONS_EXTENSION_VERSION'
          value: '~4'
        }
        {
          name:  'FUNCTIONS_WORKER_RUNTIME'
          value: 'dotnet-isolated'
        }
        // ── Storage (identity-based — no connection string) ───────────────────
        // Requires Storage Blob Data Owner + Queue + Table Contributor on the
        // func storage account (see role assignments in main.bicep).
        {
          name:  'AzureWebJobsStorage__accountName'
          value: funcStorageAccountName
        }
        {
          name:  'AzureWebJobsStorage__blobServiceUri'
          // environment().suffixes.storage resolves correctly across Azure clouds.
          value: 'https://${funcStorageAccountName}.blob.${environment().suffixes.storage}'
        }
        {
          name:  'AzureWebJobsStorage__queueServiceUri'
          value: 'https://${funcStorageAccountName}.queue.${environment().suffixes.storage}'
        }
        {
          name:  'AzureWebJobsStorage__tableServiceUri'
          value: 'https://${funcStorageAccountName}.table.${environment().suffixes.storage}'
        }
        // ── Observability ─────────────────────────────────────────────────────
        {
          name:  'APPLICATIONINSIGHTS_CONNECTION_STRING'
          value: appInsightsConnStr
        }
        // ── Cosmos DB (Managed Identity — endpoint only, no key) ──────────────
        // Key MUST be Cosmos__AccountEndpoint — matches CosmosOptions.SectionName
        // ("Cosmos") + property name (AccountEndpoint). Empty endpoint here means
        // CosmosClient construction throws at host startup (SP1-16 caught this).
        {
          name:  'Cosmos__AccountEndpoint'
          value: cosmosEndpoint
        }
        // ── Event Grid ────────────────────────────────────────────────────────
        {
          name:  'EventGrid__TopicEndpoint'
          value: eventGridTopicEndpoint
        }
        // ── SignalR (connection string stored in Key Vault) ────────────────────
        // Secret name: AzureSignalRConnectionString
        // Set via: az keyvault secret set --vault-name <kv> --name AzureSignalRConnectionString --value <conn-str>
        {
          name:  'AzureSignalRConnectionString'
          value: kvRef(keyVaultName, 'AzureSignalRConnectionString')
        }
        // ── TfNSW API key (stored in Key Vault) ───────────────────────────────
        // Secret name: TfNswApiKey
        // Set via: az keyvault secret set --vault-name <kv> --name TfNswApiKey --value <api-key>
        {
          name:  'TfNsw__ApiKey'
          value: kvRef(keyVaultName, 'TfNswApiKey')
        }
        // ── Service Bus (connection string stored in Key Vault) ────────────────
        // Secret name: ServiceBusConnectionString
        {
          name:  'ServiceBus__ConnectionString'
          value: kvRef(keyVaultName, 'ServiceBusConnectionString')
        }
        // ── Archive (ADR-0012) ────────────────────────────────────────────────
        // Data Lake account hostname; BlobServiceClient builds the full URL.
        // Other Archive__* settings use code-side defaults from ArchiveOptions.
        {
          name:  'Archive__DataLakeAccountName'
          value: dataLakeStorageAccountName
        }
      ]
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

// Principal ID is used by main.bicep to wire role assignments.
output principalId string = funcApp.identity.principalId
output funcAppName string = funcApp.name
output funcAppDefaultHostname string = funcApp.properties.defaultHostName
