// Program.cs
// ----------
// Isolated-worker host startup. Registers DI services and configures the
// Functions middleware pipeline.

using Azure.Identity;
using Azure.Messaging.EventGrid;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using SydneyPulse.Core.TfNsw;
using SydneyPulse.Functions;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication()
    .ConfigureServices((context, services) =>
    {
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();

        // Bind TfNsw config section to TfNswOptions (ApiKey comes from Key Vault reference).
        services.Configure<TfNswOptions>(
            context.Configuration.GetSection(TfNswOptions.SectionName));

        // Named HttpClient for TfNSW with standard Polly resilience pipeline:
        // retries on 429/503/transient errors with exponential backoff (SP1-04 risk mitigation).
        services.AddHttpClient("TfNsw")
                .AddStandardResilienceHandler();

        // Singleton so the 1-hour in-memory route cache is shared across
        // all Function invocations within the same host instance (ADR-0009).
        services.AddSingleton<ITfNswFeedClient, TfNswFeedClient>();

        // Bind EventGrid config section (TopicEndpoint sourced from app setting
        // EventGrid__TopicEndpoint, set in compute.bicep).
        services.Configure<EventGridOptions>(
            context.Configuration.GetSection(EventGridOptions.SectionName));

        // Singleton publisher: DefaultAzureCredential resolves to Managed Identity
        // in Azure and to `az login` credentials locally — no connection string needed.
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<EventGridOptions>>().Value;
            return new EventGridPublisherClient(
                new Uri(opts.TopicEndpoint),
                new DefaultAzureCredential());
        });

        // Bind Cosmos config section (AccountEndpoint sourced from app setting
        // Cosmos__AccountEndpoint, set in compute.bicep).
        services.Configure<CosmosOptions>(
            context.Configuration.GetSection(CosmosOptions.SectionName));

        // In-process MemoryCache used by VehiclesFunction to avoid redundant Cosmos
        // reads within the same 30-second poll cycle (5-second TTL per cache entry).
        services.AddMemoryCache();

        // Singleton CosmosClient: manages the internal connection pool.
        // DefaultAzureCredential → Managed Identity in Azure, az login locally.
        // CamelCase serialization so C# PascalCase maps to Cosmos camelCase JSON,
        // including "Id" → "id" (Cosmos required field).
        services.AddSingleton(sp =>
        {
            var opts = sp.GetRequiredService<IOptions<CosmosOptions>>().Value;
            return new CosmosClient(opts.AccountEndpoint, new DefaultAzureCredential(),
                new CosmosClientOptions
                {
                    SerializerOptions = new CosmosSerializationOptions
                    {
                        PropertyNamingPolicy = CosmosPropertyNamingPolicy.CamelCase,
                    },
                });
        });
    })
    .Build();

host.Run();
