using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Configuration;

public sealed record ConfigurationStoreDefinition
{
    public ConfigurationStoreDefinition(
        string id,
        string name,
        IReadOnlyList<ConfigurationEntry>? entries = null,
        string? endpoint = null,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? displayName = null)
    {
        Id = id;
        Name = name;
        Entries = entries ?? [];
        Endpoint = endpoint;
        HealthChecks = healthChecks ?? [];
        DisplayName = displayName;
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyList<ConfigurationEntry> Entries { get; init; }

    public string? Endpoint { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}
