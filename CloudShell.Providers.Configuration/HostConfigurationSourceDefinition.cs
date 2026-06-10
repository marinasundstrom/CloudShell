namespace CloudShell.Providers.Configuration;

public sealed record HostConfigurationSourceDefinition
{
    public HostConfigurationSourceDefinition(
        string id,
        string name,
        IReadOnlyList<string>? entries = null)
    {
        Id = id;
        Name = name;
        Entries = entries ?? [];
    }

    public string Id { get; init; }

    public string Name { get; init; }

    public IReadOnlyList<string> Entries { get; init; }
}
