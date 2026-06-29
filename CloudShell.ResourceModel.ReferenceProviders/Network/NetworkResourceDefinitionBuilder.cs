namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class NetworkResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<NetworkResourceDefinitionBuilder>(name)
{
    protected override ResourceTypeId TypeId =>
        NetworkResourceTypeProvider.ResourceTypeId;

    protected override string? ProviderId =>
        NetworkResourceTypeProvider.ProviderId;

    public NetworkResourceDefinitionBuilder WithNetworkKind(string networkKind)
        => SetScalarAttribute(NetworkResourceTypeProvider.Attributes.NetworkKind, networkKind);

    public NetworkResourceDefinitionBuilder WithHostReadiness(string hostReadiness)
        => SetScalarAttribute(NetworkResourceTypeProvider.Attributes.HostReadiness, hostReadiness);

    public NetworkResourceDefinitionBuilder WithMappingProviders(params string[] mappingProviders)
    {
        ArgumentNullException.ThrowIfNull(mappingProviders);

        return SetScalarAttribute(
            NetworkResourceTypeProvider.Attributes.MappingProviders,
            string.Join(
                ",",
                mappingProviders
                    .Where(provider => !string.IsNullOrWhiteSpace(provider))
                    .Select(provider => provider.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)));
    }
}

public static class NetworkResourceDefinitionBuilderExtensions
{
    public static NetworkResourceDefinitionBuilder AddNetwork(
        this ResourceDefinitionGraphBuilder graph,
        string name)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new NetworkResourceDefinitionBuilder(name);
        graph.Add(builder);
        return builder;
    }
}
