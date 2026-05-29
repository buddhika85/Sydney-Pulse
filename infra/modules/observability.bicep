// observability.bicep
// -------------------
// Provisions Log Analytics workspace and Application Insights for Sydney Pulse.
// App Insights is workspace-based (classic mode is deprecated).
// Sampling is fixed at 5% with a 1 GB/day cap — disabling sampling without
// coordination will exhaust the free tier in days (see CLAUDE.md constraints).

@description('Azure region.')
param location string

@description('Log Analytics workspace resource name.')
param logAnalyticsName string

@description('Application Insights resource name.')
param appInsightsName string

@description('Resource tags.')
param tags object

// ── Log Analytics workspace ───────────────────────────────────────────────────

resource logAnalytics 'Microsoft.OperationalInsights/workspaces@2022-10-01' = {
  name: logAnalyticsName
  location: location
  tags: tags
  properties: {
    sku: {
      // PerGB2018 is pay-as-you-go; first 5 GB/month free.
      name: 'PerGB2018'
    }
    retentionInDays: 30
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery:     'Enabled'
  }
}

// ── Application Insights ──────────────────────────────────────────────────────

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  tags: tags
  // Web kind is correct for both HTTP APIs and background workers.
  kind: 'web'
  properties: {
    Application_Type: 'web'
    // Workspace-based mode: telemetry lands in Log Analytics for KQL queries.
    WorkspaceResourceId: logAnalytics.id
    // Disable classic ingestion endpoint — workspace-based only.
    IngestionMode: 'LogAnalytics'
    publicNetworkAccessForIngestion: 'Enabled'
    publicNetworkAccessForQuery:     'Enabled'
  }
}

// Daily cap and proactive detection settings are NOT set here.
// The currentbillingfeatures@2015-05-01 and ProactiveDiagnosticSettings@2018-05-01-preview
// APIs return 404 BadRequest on this subscription at deploy time.
// Set the daily cap manually after deploy:
//   az monitor app-insights component billing update \
//     --app <appInsightsName> --resource-group <rg> --cap <dailyCapGb>

// ── Outputs ───────────────────────────────────────────────────────────────────

// Connection string (not instrumentation key) is the modern way to configure
// the App Insights SDK in isolated-worker Functions.
output appInsightsConnectionString string = appInsights.properties.ConnectionString
output appInsightsName string = appInsights.name
output logAnalyticsWorkspaceId string = logAnalytics.id
