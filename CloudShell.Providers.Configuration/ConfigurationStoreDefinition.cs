using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Configuration;

public sealed record ConfigurationStoreDefinition
{
    public ConfigurationStoreDefinition(
        string id,
        string name,
        IReadOnlyList<ConfigurationEntry>? entries = null,
        string? accessToken = null,
        string? endpoint = null,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null)
    {
        Id = id;
        Name = name;
        Entries = entries ?? [];
        AccessToken = accessToken;
        Endpoint = endpoint;
        HealthChecks = healthChecks ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<ConfigurationEntry> Entries { get; init; }

    public string? AccessToken { get; init; }

    public string? Endpoint { get; init; }

    public IReadOnlyList<ResourceHealthCheck> HealthChecks { get; init; }
}
