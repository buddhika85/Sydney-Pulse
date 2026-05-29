// EventGridOptions.cs
// -------------------
// Strongly-typed config for the Event Grid custom topic, bound via IOptions<EventGridOptions>.
// Registered in Program.cs: services.Configure<EventGridOptions>(config.GetSection(EventGridOptions.SectionName))
// App setting name: EventGrid__TopicEndpoint (double-underscore = nested section in Azure config).

namespace SydneyPulse.Functions;

public class EventGridOptions
{
    public const string SectionName = "EventGrid";

    // HTTPS endpoint of the Event Grid custom topic.
    // In Azure: app setting EventGrid__TopicEndpoint (set in compute.bicep).
    // Locally: set EventGrid__TopicEndpoint in local.settings.json.
    public string TopicEndpoint { get; set; } = string.Empty;
}
