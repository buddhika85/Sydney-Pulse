// frontend.bicep
// --------------
// Provisions the SignalR Service (Free SKU, Serverless mode) and the
// Azure Static Web App that hosts the Angular frontend.
//
// SignalR was manually provisioned in SP1-02 to de-risk the spike.
// This declaration brings it under Bicep management — the deployment is
// idempotent and will no-op if the resource already matches this spec.
// Free SKU caps: 20 concurrent connections, 20k messages/day (ADR-0008).
// Do NOT load-test the live dashboard against the Free SKU.

@description('Azure region.')
param location string

@description('SignalR Service resource name.')
param signalRName string

@description('SignalR Service SKU. Free_F1 for dev and portfolio-scale prod.')
param signalRSku string

@description('Static Web App resource name.')
param swaName string

@description('Resource tags.')
param tags object

// ── SignalR Service ───────────────────────────────────────────────────────────

resource signalR 'Microsoft.SignalRService/signalR@2023-02-01' = {
  name: signalRName
  location: location
  tags: tags
  sku: {
    name:     signalRSku
    capacity: 1 // Free SKU supports only 1 unit.
  }
  properties: {
    features: [
      {
        // Serverless mode: clients connect via negotiate endpoint on the
        // Function App rather than a persistent hub connection.
        flag:  'ServiceMode'
        value: 'Serverless'
      }
      {
        // Enables the client to receive messages without an active server connection.
        flag:  'EnableConnectivityLogs'
        value: 'true'
      }
    ]
    cors: {
      // Allow SWA origin and local dev origins.
      // Tighten to the specific SWA hostname post-deployment if needed.
      allowedOrigins: ['*']
    }
    // Upstream settings not configured — Serverless mode uses the Function
    // App's negotiate + output binding pattern instead.
  }
}

// ── Static Web App ────────────────────────────────────────────────────────────

resource swa 'Microsoft.Web/staticSites@2023-01-01' = {
  name: swaName
  // SWA management plane location. Content is served globally via CDN
  // regardless of this value.
  location: location
  tags: tags
  sku: {
    // Free tier: 100 GB bandwidth/month, custom domains, SSL included.
    // Sufficient for portfolio-scale traffic.
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    // Build provider is set to 'Custom' so GitHub Actions controls the
    // deployment workflow (SP1-12) rather than SWA's built-in CI.
    provider: 'Custom'
    // Branch and repository are wired up via GitHub Actions in SP1-12,
    // not at resource-creation time.
    stagingEnvironmentPolicy: 'Enabled'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output signalRHostname string = signalR.properties.hostName
output swaDefaultHostname string = swa.properties.defaultHostname
output swaName string = swa.name
