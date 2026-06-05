// messaging.bicep
// ---------------
// Provisions the Event Grid custom topic and the Service Bus topic for
// Sydney Pulse.
//
// Event Grid fans out all events from the Poller to three subscribers
// (state-writer, alerter, archiver) using event type filters (ADR-0001).
//
// The Service Bus topic lives on the pre-existing Standard namespace
// (devpulse-service-bus in DevPulseRG) — namespace-level config is never
// modified; we only add the new topic and subscription (ADR-0003).

@description('Azure region.')
param location string

@description('Event Grid custom topic resource name.')
param eventGridTopicName string

@description('Name of the pre-existing Service Bus Standard namespace.')
param existingServiceBusNamespaceName string

@description('Resource group containing the pre-existing Service Bus namespace.')
param existingServiceBusResourceGroup string

@description('Function App default hostname (e.g. sydney-pulse-func-dev.azurewebsites.net). Used for the archiver webhook endpoint.')
param funcAppDefaultHostname string = ''

@description('Function App system key for Event Grid webhook authentication. Pass listKeys() output from main.bicep. Empty string skips archiver subscription declaration (legacy path).')
@secure()
param funcAppEventGridSystemKey string = ''

@description('Resource tags.')
param tags object

// ── Event Grid custom topic ───────────────────────────────────────────────────

resource eventGridTopic 'Microsoft.EventGrid/topics@2022-06-15' = {
  name: eventGridTopicName
  location: location
  tags: tags
  properties: {
    // CloudEvents schema matches the record types in SydneyPulse.Core/Events/.
    inputSchema: 'CloudEventSchemaV1_0'
    publicNetworkAccess: 'Enabled'
  }
}

// ── Event Grid subscriptions ──────────────────────────────────────────────────
// Subscriptions are declared here as child resources of the topic.
// Delivery endpoints (Function URLs) are not known at Bicep time — they are
// set by the compute module after the Function App is deployed.
// These subscriptions use dead-letter storage on the Functions storage account;
// the storage account name is threaded through main.bicep at deploy time.

// state-writer webhook subscription is NOT declared here.
// Event Grid validates webhook endpoints at subscription creation time —
// the placeholder URL causes a 404 validation failure and aborts the deploy.
// state-writer subscription is created via az CLI in SP1-06 once the Function App
// URL is known (see infra/DEPLOY.md Step 5).
//
// archiver subscription IS declared below (ADR-0012, SP1-15). The webhook URL
// is built from funcAppDefaultHostname + funcAppEventGridSystemKey passed in
// from main.bicep, so EG can validate at deploy time. The conditional `if` lets
// initial deploys (before the Function App is provisioned) skip this subscription
// — pass empty strings for both params during the bootstrap deploy.

// archiver subscription: delivers BOTH VehicleUpdate.v1 and ServiceAlert.v1
// to ArchiverIngestFunction via WebHook for batch Parquet writes.
resource archiverSubscription 'Microsoft.EventGrid/topics/eventSubscriptions@2022-06-15' = if (!empty(funcAppDefaultHostname) && !empty(funcAppEventGridSystemKey)) {
  parent: eventGridTopic
  name: 'archiver'
  properties: {
    filter: {
      includedEventTypes: [
        'com.sydneypulse.VehicleUpdate.v1'
        'com.sydneypulse.ServiceAlert.v1'
      ]
    }
    destination: {
      endpointType: 'WebHook'
      properties: {
        endpointUrl: 'https://${funcAppDefaultHostname}/runtime/webhooks/EventGrid?functionName=ArchiverIngest&code=${funcAppEventGridSystemKey}'
      }
    }
    retryPolicy: {
      maxDeliveryAttempts:      30
      eventTimeToLiveInMinutes: 1440
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
  }
}

// alerter subscription: delivers ServiceAlert.v1 to the Service Bus topic.
resource alerterSubscription 'Microsoft.EventGrid/topics/eventSubscriptions@2022-06-15' = {
  parent: eventGridTopic
  name: 'alerter'
  properties: {
    filter: {
      includedEventTypes: [ 'com.sydneypulse.ServiceAlert.v1' ]
    }
    destination: {
      endpointType: 'ServiceBusTopic'
      properties: {
        // Compute the SB topic resource ID directly — avoids a cross-scope
        // parent reference which Bicep forbids (BCP165). The topic is created
        // by the servicebus-topic module in main.bicep before this subscription.
        resourceId: resourceId(
          existingServiceBusResourceGroup,
          'Microsoft.ServiceBus/namespaces/topics',
          existingServiceBusNamespaceName,
          'sydney-pulse-alerts'
        )
      }
    }
    retryPolicy: {
      maxDeliveryAttempts:      30
      eventTimeToLiveInMinutes: 1440
    }
    eventDeliverySchema: 'CloudEventSchemaV1_0'
  }
}

// ── Outputs ───────────────────────────────────────────────────────────────────
// Service Bus topic and subscription are created by modules/servicebus-topic.bicep
// scoped to the Service Bus resource group, and called from main.bicep.

output eventGridTopicEndpoint string = eventGridTopic.properties.endpoint
output eventGridTopicName string = eventGridTopic.name
