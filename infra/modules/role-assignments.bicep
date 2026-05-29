// role-assignments.bicep
// ----------------------
// Wires the Function App's system-assigned Managed Identity to every resource
// it needs to access. Called from main.bicep after the compute module so that
// the Function App principal ID is available.
//
// Role assignment resource names are GUIDs derived from the resource ID +
// principal ID — deterministic so re-deployments are idempotent.
// Declared inside a module so Bicep can compute the names at module-eval
// time rather than requiring them to be pre-computable in the parent scope.

@description('Principal ID of the Function App system-assigned Managed Identity.')
param functionPrincipalId string

@description('Key Vault resource name.')
param keyVaultName string

@description('Cosmos DB account resource name.')
param cosmosAccountName string

@description('Event Grid custom topic resource name.')
param eventGridTopicName string

@description('Data Lake Storage Gen2 account resource name.')
param dataLakeStorageName string

@description('Functions host storage account resource name.')
param funcStorageName string

// ── Existing resource references ──────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' existing = {
  name: keyVaultName
}

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' existing = {
  name: cosmosAccountName
}

resource eventGridTopic 'Microsoft.EventGrid/topics@2022-06-15' existing = {
  name: eventGridTopicName
}

resource dataLake 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: dataLakeStorageName
}

resource funcStorage 'Microsoft.Storage/storageAccounts@2023-01-01' existing = {
  name: funcStorageName
}

// ── Role definition IDs (subscription-scope constants) ────────────────────────

var keyVaultSecretsUser        = '4633458b-17de-408a-b874-0445c86b69e6'
var cosmosDbDataContributor    = '00000000-0000-0000-0000-000000000002'
var eventGridDataSender        = 'd5a91429-5739-47e2-a06b-3470a27159e7'
var storageBlobDataContributor = 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
var storageBlobDataOwner       = 'b7e6dc6d-f1e8-4753-8033-0f276bb0955b'
var storageQueueDataContributor= '974c5e8b-45b9-4653-ba55-5f855dd0fb88'
var storageTableDataContributor= '0a9a7e1f-b9d0-4cc4-a60d-0319b160aaa3'

// ── Key Vault Secrets User ────────────────────────────────────────────────────
// Lets the Function App read secrets (@Microsoft.KeyVault refs) at runtime.

resource kvRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, functionPrincipalId, keyVaultSecretsUser)
  scope: keyVault
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', keyVaultSecretsUser)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}

// ── Cosmos DB Built-in Data Contributor ──────────────────────────────────────
// Data-plane read/write to vehicles and alerts containers.

resource cosmosRole 'Microsoft.DocumentDB/databaseAccounts/sqlRoleAssignments@2023-04-15' = {
  name: guid(cosmos.id, functionPrincipalId, cosmosDbDataContributor)
  parent: cosmos
  properties: {
    roleDefinitionId: '${cosmos.id}/sqlRoleDefinitions/${cosmosDbDataContributor}'
    principalId:      functionPrincipalId
    scope:            cosmos.id
  }
}

// ── EventGrid Data Sender ────────────────────────────────────────────────────
// Lets the Poller Function publish CloudEvents to the custom topic.

resource egRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(eventGridTopic.id, functionPrincipalId, eventGridDataSender)
  scope: eventGridTopic
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', eventGridDataSender)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}

// ── Storage Blob Data Contributor on Data Lake ────────────────────────────────
// Lets the Archiver Function write Parquet files to the archive container.

resource dataLakeRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(dataLake.id, functionPrincipalId, storageBlobDataContributor)
  scope: dataLake
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataContributor)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}

// ── Functions host storage roles (identity-based AzureWebJobsStorage) ─────────
// Blob Data Owner + Queue + Table Contributor are all required for the
// Functions runtime to manage its internal state without a connection string.

resource funcStorageBlobRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(funcStorage.id, functionPrincipalId, storageBlobDataOwner)
  scope: funcStorage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageBlobDataOwner)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}

resource funcStorageQueueRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(funcStorage.id, functionPrincipalId, storageQueueDataContributor)
  scope: funcStorage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageQueueDataContributor)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}

resource funcStorageTableRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(funcStorage.id, functionPrincipalId, storageTableDataContributor)
  scope: funcStorage
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', storageTableDataContributor)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}
