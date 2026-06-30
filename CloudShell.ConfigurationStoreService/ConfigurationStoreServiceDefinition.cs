namespace CloudShell.ConfigurationStoreService;

public sealed record ConfigurationStoreDefinition
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<ConfigurationEntry> Entries { get; init; } = [];
}

public sealed record ConfigurationEntry(
    string Name,
    string Value,
    bool IsSecret = false);

public sealed record ConfigurationEntryResponse(
    string Name,
    string Value,
    bool IsSecret);
