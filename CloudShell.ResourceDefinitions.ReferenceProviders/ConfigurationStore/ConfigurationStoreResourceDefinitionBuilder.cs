namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ConfigurationStoreResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<ConfigurationStoreResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        ConfigurationStoreResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        ConfigurationStoreResourceTypeProvider.ProviderId;

    public ConfigurationStoreResourceDefinitionBuilder WithEndpoint(string endpoint) =>
        SetScalarAttribute(ConfigurationStoreResourceTypeProvider.Attributes.Endpoint, endpoint);
}

public static class ConfigurationStoreResourceDefinitionBuilderExtensions
{
    public static ConfigurationStoreResourceDefinitionBuilder AddConfigurationStore(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new ConfigurationStoreResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
