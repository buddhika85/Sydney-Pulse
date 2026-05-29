// security.bicep
// --------------
// Provisions the Key Vault for Sydney Pulse.
// All application secrets (TfNSW API key, SignalR connection string) are
// stored here and accessed by the Function App via Managed Identity at
// runtime — never via connection strings in app settings.
//
// Role assignments are in main.bicep (declared after compute so the
// Function App's principal ID is available).

@description('Azure region.')
param location string

@description('Key Vault resource name.')
param keyVaultName string

@description('Resource tags.')
param tags object

// ── Key Vault ─────────────────────────────────────────────────────────────────

resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: keyVaultName
  location: location
  tags: tags
  properties: {
    sku: {
      family: 'A'
      // Standard tier — portfolio scale does not need HSM-backed keys.
      name: 'standard'
    }
    tenantId: subscription().tenantId

    // Managed Identity access only — no legacy access policies.
    enableRbacAuthorization: true

    // Soft-delete with 7-day retention (minimum allowed).
    // Keeps secrets recoverable after accidental deletion.
    enableSoftDelete: true
    softDeleteRetentionInDays: 7

    // Public network access is on; no VNet at portfolio scale (see ADR-0006).
    publicNetworkAccess: 'Enabled'
    networkAcls: {
      defaultAction: 'Allow'
      bypass: 'AzureServices'
    }
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

// URI used by compute.bicep to construct @Microsoft.KeyVault(...) references.
output keyVaultUri string = keyVault.properties.vaultUri
output keyVaultName string = keyVault.name
