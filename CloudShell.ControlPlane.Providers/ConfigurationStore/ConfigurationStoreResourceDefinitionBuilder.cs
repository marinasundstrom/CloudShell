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
