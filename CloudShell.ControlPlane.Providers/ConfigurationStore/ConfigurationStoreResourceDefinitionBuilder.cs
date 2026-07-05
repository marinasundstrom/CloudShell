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

    public ConfigurationStoreResourceDefinitionBuilder WithSeed(
        Action<ConfigurationStoreSeedBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var seed = new ConfigurationStoreSeedBuilder();
        configure(seed);
        if (seed.Settings.Count > 0)
        {
            SetObjectAttribute(
                ConfigurationStoreResourceTypeProvider.Attributes.Settings,
                seed.Settings);
        }

        return this;
    }

    public ResourceConfigurationSettingReference Setting(
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

public sealed class ConfigurationStoreSeedBuilder
{
    private readonly List<ConfigurationStoreSeedSetting> _settings = [];

    public IReadOnlyList<ConfigurationStoreSeedSetting> Settings => _settings;

    public ConfigurationStoreSeedBuilder Setting(
        string name,
        string value)
    {
        _settings.Add(new(name, value));
        return this;
    }
}

public sealed record ConfigurationStoreSeedSetting(
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
