namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ConfigurationStoreResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        ConfigurationStoreResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ConfigurationStoreResourceTypeProvider.ProviderId;

    public ConfigurationStoreResourceDefinitionBuilder WithRuntimeMonitoring() =>
        this;

    public ConfigurationStoreResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint, endpoint);

    public ConfigurationStoreResourceDefinitionBuilder WithEntry(
        string name,
        string value) =>
        WithSetting(name, value);

    public ConfigurationStoreResourceDefinitionBuilder WithEntry(
        ConfigurationStoreSeedEntry entry) =>
        WithSetting(new ConfigurationStoreSettingEntry(entry.Name, entry.Value));

    public ConfigurationStoreResourceDefinitionBuilder WithSetting(
        string name,
        string value) =>
        WithSetting(new ConfigurationStoreSettingEntry(name, value));

    public ConfigurationStoreResourceDefinitionBuilder WithSetting(
        ConfigurationStoreSettingEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entries = Attributes.TryGetValue(
                ConfigurationStoreResourceTypeProvider.Attributes.Entries,
                out var currentEntries)
            ? currentEntries.ToObject<ConfigurationStoreSettingEntry[]>() ?? []
            : [];
        return SetObjectAttribute(
            ConfigurationStoreResourceTypeProvider.Attributes.Entries,
            entries.Append(entry).ToArray());
    }

    public ConfigurationStoreResourceDefinitionBuilder WithEntries(
        IEnumerable<ConfigurationStoreSeedEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return WithSettings(entries.Select(entry => new ConfigurationStoreSettingEntry(entry.Name, entry.Value)));
    }

    public ConfigurationStoreResourceDefinitionBuilder WithSettings(
        IEnumerable<ConfigurationStoreSettingEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        return SetObjectAttribute(
            ConfigurationStoreResourceTypeProvider.Attributes.Entries,
            entries.ToArray());
    }

    public ResourceConfigurationEntryReference Entry(
        string name,
        string? version = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return new(
            EffectiveResourceId,
            name.Trim(),
            string.IsNullOrWhiteSpace(version) ? null : version.Trim());
    }
}

public sealed record ConfigurationStoreSeedEntry(
    string Name,
    string Value);

public sealed record ConfigurationStoreSettingEntry(
    string Name,
    string Value);

public static class ConfigurationStoreResourceDefinitionBuilderExtensions
{
    public static ConfigurationStoreResourceDefinitionBuilder AddConfigurationStore(
        this ResourceGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ConfigurationStoreResourceDefinitionBuilder(name)
            .WithRuntimeMonitoring();
        graph.Add(builder);
        return builder;
    }
}
