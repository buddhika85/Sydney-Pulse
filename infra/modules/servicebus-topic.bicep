// servicebus-topic.bicep
// ----------------------
// Creates the sydney-pulse-alerts topic and alerter-sub subscription on the
// pre-existing Service Bus Standard namespace (ADR-0003).
//
// This module is deployed with scope: resourceGroup(existingServiceBusResourceGroup)
// in main.bicep because Bicep cannot deploy child resources when the parent
// `existing` resource has a cross-scope reference (BCP165).
//
// Only adds new child resources — never modifies namespace-level configuration.

// Scoped to the Service Bus resource group, not the Sydney Pulse RG.
targetScope = 'resourceGroup'

@description('Name of the pre-existing Service Bus Standard namespace.')
param existingServiceBusNamespaceName string

// ── Existing namespace reference ──────────────────────────────────────────────
// No scope needed here — this module is already deployed to the correct RG.

resource sbNamespace 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' existing = {
  name: existingServiceBusNamespaceName
}

// ── Service Bus topic ─────────────────────────────────────────────────────────

resource alertsTopic 'Microsoft.ServiceBus/namespaces/topics@2022-10-01-preview' = {
  parent: sbNamespace
  name: 'sydney-pulse-alerts'
  // Note: tags not supported on SB topic resources — applied at namespace level only.
  properties: {
    maxSizeInMegabytes:         1024  // 1 GB; alert volume is low at portfolio scale.
    defaultMessageTimeToLive:   'P1D' // 24-hour TTL; stale alerts are not redelivered.
    requiresDuplicateDetection: false
    enableBatchedOperations:    true
  }
}

// ── Subscription the Alerter Function listens on ──────────────────────────────

resource alerterSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' = {
  parent: alertsTopic
  name: 'alerter-sub'
  properties: {
    maxDeliveryCount:                 5       // Message goes to DLQ after 5 failures.
    lockDuration:                     'PT1M'
    defaultMessageTimeToLive:         'P1D'
    deadLetteringOnMessageExpiration: true
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────

output alertsTopicName string = alertsTopic.name
output alertsTopicId string = alertsTopic.id
