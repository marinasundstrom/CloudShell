namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class VirtualNetworkResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<VirtualNetworkResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        VirtualNetworkResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        VirtualNetworkResourceTypeProvider.ProviderId;

    public VirtualNetworkResourceDefinitionBuilder WithNetworkKind(string networkKind) =>
        SetScalarAttribute(VirtualNetworkResourceTypeProvider.Attributes.NetworkKind, networkKind);

    public VirtualNetworkResourceDefinitionBuilder AsDefault(bool isDefault = true) =>
        SetScalarAttribute(VirtualNetworkResourceTypeProvider.Attributes.IsDefault, isDefault);

    public VirtualNetworkResourceDefinitionBuilder WithHostReadiness(string hostReadiness) =>
        SetScalarAttribute(VirtualNetworkResourceTypeProvider.Attributes.HostReadiness, hostReadiness);

    public VirtualNetworkResourceDefinitionBuilder WithMappingProviders(params string[] mappingProviders)
    {
        ArgumentNullException.ThrowIfNull(mappingProviders);

        return SetScalarAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.MappingProviders,
            string.Join(
                ",",
                mappingProviders
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Select(provider => provider.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)));
    }
}

public static class VirtualNetworkResourceDefinitionBuilderExtensions
{
    public static VirtualNetworkResourceDefinitionBuilder AddVirtualNetwork(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new VirtualNetworkResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
