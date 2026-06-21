namespace CloudShell.Providers.Configuration;

public sealed record HostConfigurationSourceDefinition
{
    public HostConfigurationSourceDefinition(
        string id,
        string name,
        IReadOnlyList<string>? entries = null,
        string? displayName = null)
    {
        Id = id;
        Name = name;
        Entries = entries ?? [];
        DisplayName = displayName;
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public string? DisplayName { get; init; }

    public IReadOnlyList<string> Entries { get; init; }
}
