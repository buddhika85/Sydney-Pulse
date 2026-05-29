// Program.cs
// ----------
// Isolated-worker host startup. Registers DI services and configures the
// Functions middleware pipeline.

using Azure.Identity;
using Azure.Messaging.EventGrid;
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
    })
    .Build();

host.Run();
