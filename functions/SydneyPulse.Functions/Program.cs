// Program.cs
// ----------
// Isolated-worker host startup. Registers DI services and configures the
// Functions middleware pipeline.

using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SydneyPulse.Core.TfNsw;

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
    })
    .Build();

host.Run();
