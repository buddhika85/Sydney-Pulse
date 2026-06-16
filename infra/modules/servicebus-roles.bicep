// servicebus-roles.bicep
// ----------------------
// Grants the Function App's Managed Identity Data Receiver on the
// alerter-sub. Lives in its own module so it can run AFTER compute (when
// the Function App MI exists) while still being scoped to the shared
// SB namespace resource group (ADR-0003).
//
// Least-privilege scope: the subscription, not the namespace.

targetScope = 'resourceGroup'

@description('Name of the pre-existing Service Bus Standard namespace.')
param existingServiceBusNamespaceName string

@description('Service Bus topic name (created by servicebus-topic.bicep).')
param topicName string

@description('Service Bus subscription name the Alerter consumes from.')
param subscriptionName string

@description('Function App system-assigned MI principal ID.')
param functionPrincipalId string

// Built-in role definition GUID (verify with `az role definition list`).
var azureServiceBusDataReceiver = '4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0'

// Reference the existing subscription so the role's scope can target it.
resource alerterSub 'Microsoft.ServiceBus/namespaces/topics/subscriptions@2022-10-01-preview' existing = {
  name: '${existingServiceBusNamespaceName}/${topicName}/${subscriptionName}'
}

resource sbReceiverRole 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(alerterSub.id, functionPrincipalId, azureServiceBusDataReceiver)
  scope: alerterSub
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', azureServiceBusDataReceiver)
    principalId:      functionPrincipalId
    principalType:    'ServicePrincipal'
  }
}
