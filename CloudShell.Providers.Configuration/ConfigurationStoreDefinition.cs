namespace CloudShell.Providers.Configuration;

public sealed record ConfigurationStoreDefinition
{
    public ConfigurationStoreDefinition(
        string id,
        string name,
        IReadOnlyList<ConfigurationEntry>? entries = null,
        string? accessToken = null,
        string? endpoint = null)
    {
        Id = id;
        Name = name;
        Entries = entries ?? [];
        AccessToken = accessToken;
        Endpoint = endpoint;
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<ConfigurationEntry> Entries { get; init; }

    public string? AccessToken { get; init; }

    public string? Endpoint { get; init; }
}
