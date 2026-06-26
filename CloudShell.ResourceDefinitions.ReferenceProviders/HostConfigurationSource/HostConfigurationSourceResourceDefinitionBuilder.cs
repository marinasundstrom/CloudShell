namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class HostConfigurationSourceResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<HostConfigurationSourceResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        HostConfigurationSourceResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        HostConfigurationSourceResourceTypeProvider.ProviderId;

    public HostConfigurationSourceResourceDefinitionBuilder WithSource(string source) =>
        SetScalarAttribute(HostConfigurationSourceResourceTypeProvider.Attributes.Source, source);
}

public static class HostConfigurationSourceResourceDefinitionBuilderExtensions
{
    public static HostConfigurationSourceResourceDefinitionBuilder AddHostConfigurationSource(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new HostConfigurationSourceResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
