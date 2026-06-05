# Architecture diagrams

Visual companion to [architecture.md](architecture.md).
Update this file in the same PR whenever the Azure topology changes:
new function, new data store, new messaging route, renamed resource, or rewired data flow.

## Azure topology

Full component map from TfNSW ingest through to the Angular frontend.
Dashed arrows are cross-cutting concerns (secrets, telemetry).

```mermaid
flowchart TD
    subgraph ext["External"]
        TfNSW["TfNSW Open Data API<br/>GTFS-RT protobuf · static CSV<br/>gtfs.transportnsw.info"]
    end

    subgraph ingest["Ingest"]
        FeedClient["TfNswFeedClient<br/>Core/TfNsw/TfNswFeedClient.cs<br/>Polly retry · 1 h route cache (ADR-0009)"]
        Poller["PollerFunction<br/>TimerTrigger 30 s<br/>AzFunctions/EventPipeline/PollerFunction.cs"]
    end

    subgraph messaging["Messaging"]
        EG{{"Event Grid<br/>transit-events<br/>infra/modules/messaging.bicep"}}
        SBTopic["Service Bus Topic<br/>sydney-pulse-alerts<br/>devpulse-service-bus / DevPulseRG<br/>infra/modules/servicebus-topic.bicep"]
    end

    subgraph processing["Processing — sydney-pulse-func-{env}<br/>infra/modules/compute.bicep"]
        StateWriter["StateWriterFunction<br/>EventGridTrigger · VehicleUpdate.v1<br/>AzFunctions/EventPipeline/StateWriterFunction.cs"]
        AlerterFn["AlerterFunction<br/>ServiceBusTrigger<br/>AzFunctions/EventPipeline/AlerterFunction.cs"]
        ArchiverIngestFn["ArchiverIngestFunction<br/>EventGridTrigger · all types<br/>AzFunctions/EventPipeline/ArchiverIngestFunction.cs"]
        ArchiverFlushFn["ArchiverFlushFunction<br/>TimerTrigger · every 5 min<br/>AzFunctions/EventPipeline/ArchiverFlushFunction.cs"]
        HttpApi["HTTP API Functions<br/>GET /vehicles · /alerts · /routes<br/>POST /negotiate · GET /analytics · /ops"]
    end

    subgraph storage["Data — infra/modules/data.bicep"]
        CosmosDB[("Cosmos DB Serverless<br/>sydneyPulse DB · ADR-0002<br/>vehicles TTL 5 m · alerts TTL 24 h<br/>partition key: routeShortName")]
        DataLake[("Data Lake Gen2 · ADR-0012<br/>sydpulsedlsa{env}<br/>pending/ (JSONL by hour)<br/>archive/ (Parquet + _manifest.json)")]
    end

    subgraph security["Security — infra/modules/security.bicep"]
        KeyVault[("Key Vault<br/>sydney-pulse-kv-{env}<br/>TfNswApiKey · SignalRConnectionString<br/>ServiceBusConnectionString")]
    end

    subgraph frontend["Frontend — infra/modules/frontend.bicep"]
        SignalRSvc["SignalR Service Free_F1<br/>sydney-pulse-signalr-{env} · ADR-0008<br/>groups: vehicles · alerts"]
        StaticWebApp["Azure Static Web App<br/>sydney-pulse-swa-{env}<br/>web/src/app/"]
    end

    subgraph observability["Observability — infra/modules/observability.bicep"]
        AppInsights["App Insights<br/>sydney-pulse-ai-{env}<br/>5% sampling · 1 GB/day cap"]
    end

    TfNSW -->|"GTFS-RT + static"| FeedClient
    FeedClient --> Poller
    Poller -->|"VehicleUpdate.v1<br/>ServiceAlert.v1<br/>CloudEvents"| EG

    EG -->|"VehicleUpdate.v1<br/>state-writer sub"| StateWriter
    EG -->|"ServiceAlert.v1<br/>alerter sub → SB filter"| SBTopic
    EG -->|"all types<br/>archiver-ingest sub"| ArchiverIngestFn

    StateWriter -->|"upsert by vehicleId"| CosmosDB
    StateWriter -->|"vehicleUpdated via Azure Function output binding"| SignalRSvc

    SBTopic -->|"alerter-sub"| AlerterFn
    AlerterFn -->|"alertReceived via Azure Function output binding"| SignalRSvc
    AlerterFn -->|"upsert by alertId"| CosmosDB

    ArchiverIngestFn -->|"JSONL append<br/>per hour partition"| DataLake
    ArchiverFlushFn -->|"Parquet + _manifest.json<br/>per closed partition"| DataLake

    CosmosDB -->|"read"| HttpApi
    HttpApi --> StaticWebApp
    SignalRSvc -->|"WebSocket"| StaticWebApp

    KeyVault -. "Managed Identity" .-> Poller
    KeyVault -. "Managed Identity" .-> StateWriter
    KeyVault -. "Managed Identity" .-> AlerterFn
    KeyVault -. "Managed Identity" .-> ArchiverIngestFn
    KeyVault -. "Managed Identity" .-> ArchiverFlushFn

    Poller -. "telemetry" .-> AppInsights
    StateWriter -. "telemetry" .-> AppInsights
    AlerterFn -. "telemetry" .-> AppInsights
    HttpApi -. "telemetry" .-> AppInsights
    ArchiverIngestFn -. "telemetry" .-> AppInsights
    ArchiverFlushFn -. "telemetry" .-> AppInsights

    %% Edge colours — indices match declaration order above; update comment when edges are added/removed
    %% [0-2]    Ingest: TfNSW → FeedClient → Poller → Event Grid
    linkStyle 0,1,2 stroke:#0078D4,stroke-width:2px
    %% [3-5,8]  Fan-out: Event Grid → StateWriter / SBTopic / ArchiverIngest; SBTopic → Alerter
    linkStyle 3,4,5,8 stroke:#00B294,stroke-width:2px
    %% [6,10,11,12]  Write: functions → Cosmos / Data Lake (pending + archive)
    linkStyle 6,10,11,12 stroke:#FF8C00,stroke-width:2px
    %% [7,9,15]  Real-time push: → SignalR → Angular
    linkStyle 7,9,15 stroke:#E74C3C,stroke-width:2px
    %% [13-14]  Read: Cosmos → HTTP API → Angular
    linkStyle 13,14 stroke:#107C10,stroke-width:2px
    %% [16-20]  Managed Identity: Key Vault → functions (security dependency, not main data flow)
    linkStyle 16,17,18,19,20 stroke:#D13438,stroke-width:1px,stroke-dasharray:5
    %% [21-26]  Telemetry: functions → App Insights (observability side-channel, not main data flow)
    linkStyle 21,22,23,24,25,26 stroke:#8764B8,stroke-width:1px,stroke-dasharray:5
```

### Edge colour legend

Solid lines = main data flows you follow to understand the application. Dashed lines = out-of-band concerns that are real but not part of the data story.

| Colour | Path |
|---|---|
| Blue `#0078D4` | **Ingest** — raw data from TfNSW through to Event Grid |
| Teal `#00B294` | **Event fan-out** — Event Grid routing to each subscriber |
| Amber `#FF8C00` | **Write** — durable state written to Cosmos or Data Lake |
| Red `#E74C3C` | **Real-time push** — live updates through SignalR to Angular |
| Green `#107C10` | **Read** — on-demand query from Cosmos through HTTP API |
| Red dashed `#D13438` | **Managed Identity** — Key Vault secrets pulled by functions at runtime |
| Purple dashed `#8764B8` | **Telemetry** — traces and metrics emitted to App Insights |

## Key file map

| Component | Primary file | Bicep module |
|---|---|---|
| TfNSW client + route cache | `functions/SydneyPulse.Core/TfNsw/TfNswFeedClient.cs` | — |
| Poller Function | `functions/SydneyPulse.Functions/AzFunctions/EventPipeline/PollerFunction.cs` | `compute.bicep` |
| State Writer Function | `functions/SydneyPulse.Functions/AzFunctions/EventPipeline/StateWriterFunction.cs` | `compute.bicep` |
| Alerter Function | `functions/SydneyPulse.Functions/AzFunctions/EventPipeline/AlerterFunction.cs` | `compute.bicep` |
| Archiver Ingest Function | `functions/SydneyPulse.Functions/AzFunctions/EventPipeline/ArchiverIngestFunction.cs` | `compute.bicep` |
| Archiver Flush Function | `functions/SydneyPulse.Functions/AzFunctions/EventPipeline/ArchiverFlushFunction.cs` | `compute.bicep` |
| Pending blob store (abstraction) | `functions/SydneyPulse.Functions/Archive/PendingBlobStore.cs` | — |
| Parquet writer | `functions/SydneyPulse.Core/Archive/ParquetArchiveWriter.cs` | — |
| Hive partition path helper | `functions/SydneyPulse.Core/Archive/HivePartitionPath.cs` | — |
| Archive manifest model | `functions/SydneyPulse.Core/Archive/ArchiveManifest.cs` | — |
| HTTP API Functions | `functions/SydneyPulse.Functions/AzFunctions/HttpApi/` | `compute.bicep` |
| Event Grid subscriptions | — | `messaging.bicep` |
| Service Bus topic | — | `servicebus-topic.bicep` |
| Cosmos DB containers | `functions/SydneyPulse.Core/Cosmos/` | `data.bicep` |
| Key Vault | — | `security.bicep` |
| SignalR Service | — | `frontend.bicep` |
| Static Web App | `web/src/app/` | `frontend.bicep` |
| App Insights + Log Analytics | — | `observability.bicep` |
| Role assignments (MI) | — | `role-assignments.bicep` |
