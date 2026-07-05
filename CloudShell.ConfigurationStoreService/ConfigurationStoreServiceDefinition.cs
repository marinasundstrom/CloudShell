namespace CloudShell.ConfigurationStoreService;

public sealed record ConfigurationStoreDefinition
{
    public string Id { get; init; } = string.Empty;

    public IReadOnlyList<ConfigurationSetting> Settings { get; init; } = [];
}

public sealed record ConfigurationSetting(
    string Name,
    string Value);

public sealed record ConfigurationSettingResponse(
    string Name,
    string Value);
