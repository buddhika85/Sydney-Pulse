// CosmosOptions.cs
// ----------------
// Strongly-typed config for the Cosmos DB account, bound via IOptions<CosmosOptions>. Options Pattern.
// Registered in Program.cs: services.Configure<CosmosOptions>(config.GetSection(CosmosOptions.SectionName))
// App setting name: Cosmos__AccountEndpoint (double-underscore = nested section in Azure config).

namespace SydneyPulse.Functions;

public class CosmosOptions
{
    public const string SectionName = "Cosmos";

    // HTTPS endpoint of the Cosmos DB account.
    // In Azure: app setting Cosmos__AccountEndpoint (set in compute.bicep).
    // Locally: set Cosmos__AccountEndpoint in local.settings.json.
    public string AccountEndpoint { get; set; } = string.Empty;
}
