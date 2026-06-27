namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class VirtualNetworkResourceDefinitionBuilder(string name) :
    ResourceDefinitionBuilder<VirtualNetworkResourceDefinitionBuilder>(name)
{
    private readonly List<NetworkingEndpointValue> _endpoints = [];
    private readonly List<NetworkingEndpointNetworkMappingValue> _endpointNetworkMappings = [];
    private readonly List<NetworkingEndpointMappingValue> _endpointMappings = [];

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

    public VirtualNetworkResourceDefinitionBuilder AddEndpoint(
        string name,
        string protocol,
        int targetPort,
        string exposure = "Public")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(protocol);

        var normalizedName = name.Trim();
        _endpoints.RemoveAll(endpoint =>
            string.Equals(endpoint.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        _endpoints.Add(new(
            normalizedName,
            protocol.Trim(),
            targetPort,
            exposure));
        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.Endpoints,
            _endpoints);
    }

    public VirtualNetworkResourceDefinitionBuilder AddEndpointNetworkMapping(
        string endpointName,
        string address,
        string? id = null,
        string? name = null,
        string exposure = "Public",
        IResourceDefinitionBuilder? provider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpointName);
        ArgumentException.ThrowIfNullOrWhiteSpace(address);

        var normalizedEndpointName = endpointName.Trim();
        var mappingId = string.IsNullOrWhiteSpace(id)
            ? $"{EffectiveResourceId}:endpoint-network-mapping:{normalizedEndpointName}"
            : id.Trim();
        _endpointNetworkMappings.RemoveAll(mapping =>
            string.Equals(mapping.Id, mappingId, StringComparison.OrdinalIgnoreCase));
        _endpointNetworkMappings.Add(new(
            mappingId,
            string.IsNullOrWhiteSpace(name) ? normalizedEndpointName : name.Trim(),
            new NetworkingEndpointReferenceValue(
                ResourceReference.ReferenceResourceId(
                    EffectiveResourceId,
                    VirtualNetworkResourceTypeProvider.ResourceTypeId),
                normalizedEndpointName),
            address.Trim(),
            exposure,
            ResourceReference.ReferenceResourceId(
                EffectiveResourceId,
                VirtualNetworkResourceTypeProvider.ResourceTypeId),
            provider is null
                ? null
                : ResourceReference.ReferenceResourceId(provider.EffectiveResourceId),
            normalizedEndpointName));
        if (provider is not null)
        {
            DependsOn(provider);
        }

        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.EndpointNetworkMappings,
            _endpointNetworkMappings);
    }

    public VirtualNetworkResourceDefinitionBuilder MapEndpoint(
        string sourceEndpointName,
        IResourceDefinitionBuilder target,
        string targetEndpointName,
        IResourceDefinitionBuilder? provider = null,
        string? id = null,
        string? name = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceEndpointName);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetEndpointName);

        var normalizedSourceEndpointName = sourceEndpointName.Trim();
        var mappingId = string.IsNullOrWhiteSpace(id)
            ? $"{EffectiveResourceId}:endpoint-mapping:{normalizedSourceEndpointName}:{target.EffectiveResourceId}:{targetEndpointName.Trim()}"
            : id.Trim();
        _endpointMappings.RemoveAll(mapping =>
            string.Equals(mapping.Id, mappingId, StringComparison.OrdinalIgnoreCase));
        _endpointMappings.Add(new(
            new NetworkingEndpointReferenceValue(
                ResourceReference.ReferenceResourceId(
                    EffectiveResourceId,
                    VirtualNetworkResourceTypeProvider.ResourceTypeId),
                normalizedSourceEndpointName),
            new NetworkingEndpointReferenceValue(
                ResourceReference.ReferenceResourceId(target.EffectiveResourceId),
                targetEndpointName.Trim()),
            mappingId,
            string.IsNullOrWhiteSpace(name) ? mappingId : name.Trim(),
            ResourceReference.ReferenceResourceId(
                EffectiveResourceId,
                VirtualNetworkResourceTypeProvider.ResourceTypeId),
            provider is null
                ? null
                : ResourceReference.ReferenceResourceId(provider.EffectiveResourceId)));
        DependsOn(target);
        if (provider is not null)
        {
            DependsOn(provider);
        }

        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.EndpointMappings,
            _endpointMappings);
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
