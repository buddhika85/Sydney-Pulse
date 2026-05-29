// data.bicep
// ----------
// Provisions:
//   - Cosmos DB Serverless account with vehicles + alerts containers (ADR-0002)
//   - Azure Storage account for the Functions host (AzureWebJobsStorage)
//   - Data Lake Storage Gen2 account for the Archiver Function
//
// Cosmos is a fresh account in australiaeast — not reusing devpulse-events
// (which is in australiasoutheast) to avoid cross-region latency on every
// vehicle upsert. See investigation note in docs/sprints/progress.md SP1-03.

@description('Azure region.')
param location string

@description('Cosmos DB account resource name.')
param cosmosName string

@description('Storage account name for the Functions host.')
param funcStorageName string

@description('Data Lake Storage Gen2 account name for the Archiver.')
param dataLakeName string

@description('Resource tags.')
param tags object

// ── Cosmos DB account ─────────────────────────────────────────────────────────

resource cosmos 'Microsoft.DocumentDB/databaseAccounts@2023-04-15' = {
  name: cosmosName
  location: location
  tags: tags
  kind: 'GlobalDocumentDB'
  properties: {
    databaseAccountOfferType: 'Standard'
    // Serverless — pay per RU consumed, no provisioned throughput (ADR-0002).
    capabilities: [
      { name: 'EnableServerless' }
    ]
    locations: [
      {
        locationName:     location
        failoverPriority: 0
        isZoneRedundant:  false // Zone redundancy not needed at portfolio scale.
      }
    ]
    // Session consistency: right balance of performance and correctness for
    // a single-region portfolio deployment.
    consistencyPolicy: {
      defaultConsistencyLevel: 'Session'
    }
    // RBAC-only data plane access — no primary/secondary key usage (see security.bicep).
    disableLocalAuth: false // Keep false: built-in role assignments require local auth during setup.
    enableFreeTier: false   // Free tier unavailable on this subscription (ADR-0002).
    publicNetworkAccess: 'Enabled'
  }
}

// ── Cosmos database ───────────────────────────────────────────────────────────

resource db 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases@2023-04-15' = {
  parent: cosmos
  name: 'sydney-pulse'
  properties: {
    resource: { id: 'sydney-pulse' }
    // No throughput on the database — serverless containers are individually billed.
  }
}

// ── vehicles container ────────────────────────────────────────────────────────
// Stores latest position per vehicle. TTL 5 min auto-purges stale documents.

resource vehiclesContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: db
  name: 'vehicles'
  properties: {
    resource: {
      id: 'vehicles'
      partitionKey: {
        // route_short_name (e.g. T1) is the grouping key for UI and cost (ADR-0002).
        paths: [ '/routeShortName' ]
        kind:  'Hash'
      }
      // 5-minute TTL: live state only; historical data goes to Data Lake.
      defaultTtl: 300
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [ { path: '/*' } ]
        // Exclude lat/lon: geospatial queries not needed; saves ~30% RU on upserts.
        excludedPaths: [
          { path: '/lat/?' }
          { path: '/lon/?' }
          { path: '/"_etag"/?' }
        ]
      }
    }
  }
}

// ── alerts container ──────────────────────────────────────────────────────────
// Stores active service alerts. TTL 24 h matches TfNSW alert lifecycle.

resource alertsContainer 'Microsoft.DocumentDB/databaseAccounts/sqlDatabases/containers@2023-04-15' = {
  parent: db
  name: 'alerts'
  properties: {
    resource: {
      id: 'alerts'
      partitionKey: {
        paths: [ '/routeShortName' ]
        kind:  'Hash'
      }
      defaultTtl: 86400 // 24 hours in seconds.
      indexingPolicy: {
        indexingMode: 'consistent'
        includedPaths: [ { path: '/*' } ]
        excludedPaths: [ { path: '/"_etag"/?' } ]
      }
    }
  }
}

// ── Functions host storage account ───────────────────────────────────────────
// Required by the Azure Functions runtime for internal state (triggers, locks).

resource funcStorage 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: funcStorageName
  location: location
  tags: tags
  sku: {
    // LRS is sufficient; Functions host storage is not business-critical data.
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
  properties: {
    accessTier:             'Hot'
    minimumTlsVersion:      'TLS1_2'
    allowBlobPublicAccess:  false
    supportsHttpsTrafficOnly: true
  }
}

// ── Data Lake Storage Gen2 ────────────────────────────────────────────────────
// Archiver Function writes Parquet files here for historical analytics.

resource dataLake 'Microsoft.Storage/storageAccounts@2023-01-01' = {
  name: dataLakeName
  location: location
  tags: tags
  sku: { name: 'Standard_LRS' }
  kind: 'StorageV2'
  properties: {
    accessTier:            'Hot'
    minimumTlsVersion:     'TLS1_2'
    allowBlobPublicAccess: false
    supportsHttpsTrafficOnly: true
    // isHnsEnabled = true enables the hierarchical namespace required for Gen2.
    isHnsEnabled: true
  }
}

// Archive container — layout: archive/yyyy=.../MM=.../dd=.../HH=.../events.parquet
resource archiveContainer 'Microsoft.Storage/storageAccounts/blobServices/containers@2023-01-01' = {
  name: '${dataLakeName}/default/archive'
  properties: {
    publicAccess: 'None'
  }
  dependsOn: [dataLake]
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output cosmosEndpoint string = cosmos.properties.documentEndpoint
output cosmosAccountName string = cosmos.name
output funcStorageAccountName string = funcStorage.name
output dataLakeStorageAccountName string = dataLake.name
