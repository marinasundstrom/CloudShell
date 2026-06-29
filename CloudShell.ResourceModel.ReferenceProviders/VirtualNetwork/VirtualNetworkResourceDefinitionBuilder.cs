namespace CloudShell.ResourceModel.ReferenceProviders;

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

    public VirtualNetworkResourceDefinitionBuilder WithMappingProviders(
        params IResourceDefinitionBuilder[] mappingProviders)
    {
        ArgumentNullException.ThrowIfNull(mappingProviders);

        return WithMappingProviders(
            mappingProviders
                .Where(provider => provider is not null)
                .Select(provider => provider.EffectiveResourceId)
                .ToArray());
    }

    public NetworkingEndpointReferenceValue AddTcpEndpoint(
        string host,
        int? port = null,
        string name = "tcp",
        string exposure = "Local") =>
        AddNetworkEndpoint(
            name,
            "Tcp",
            host,
            port,
            exposure);

    public NetworkingEndpointReferenceValue AddHttpEndpoint(
        string host,
        int? port = null,
        string name = "http",
        string exposure = "Local") =>
        AddNetworkEndpoint(
            name,
            "Http",
            host,
            port,
            exposure);

    public NetworkingEndpointReferenceValue RequestTcpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        string exposure = "Local") =>
        AddTcpEndpoint(
            host,
            port,
            name,
            exposure);

    public NetworkingEndpointReferenceValue RequestHttpEndpoint(
        string name,
        string host = "localhost",
        int? port = null,
        string exposure = "Local") =>
        AddHttpEndpoint(
            host,
            port,
            name,
            exposure);

    public VirtualNetworkResourceDefinitionBuilder AddEndpoint(
        string name,
        string protocol,
        int? targetPort,
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
            DependsOnIfMissing(provider);
        }

        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.EndpointNetworkMappings,
            _endpointNetworkMappings);
    }

    public VirtualNetworkResourceDefinitionBuilder MapEndpoint(
        NetworkingEndpointReferenceValue source,
        NetworkingEndpointReferenceValue target,
        IResourceDefinitionBuilder? provider = null,
        string? id = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);

        var mappingId = string.IsNullOrWhiteSpace(id)
            ? CreateEndpointMappingId(source, target)
            : id.Trim();
        _endpointMappings.RemoveAll(mapping =>
            string.Equals(mapping.Id, mappingId, StringComparison.OrdinalIgnoreCase));
        _endpointMappings.Add(new(
            source,
            target,
            mappingId,
            string.IsNullOrWhiteSpace(name)
                ? $"{source.EndpointName} to {target.EndpointName}"
                : name.Trim(),
            ResourceReference.ReferenceResourceId(
                EffectiveResourceId,
                VirtualNetworkResourceTypeProvider.ResourceTypeId),
            provider is null
                ? null
                : ResourceReference.ReferenceResourceId(provider.EffectiveResourceId)));

        AddEndpointReferenceDependencies(source, target);
        if (provider is not null)
        {
            DependsOnIfMissing(provider);
        }

        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.EndpointMappings,
            _endpointMappings);
    }

    public VirtualNetworkResourceDefinitionBuilder MapEndpoint(
        NetworkingEndpointReferenceValue source,
        IResourceDefinitionBuilder target,
        string targetEndpointName,
        IResourceDefinitionBuilder? provider = null,
        string? id = null,
        string? name = null)
    {
        ArgumentNullException.ThrowIfNull(target);

        return MapEndpoint(
            source,
            NetworkingEndpointReferenceValue.ForResource(target, targetEndpointName),
            provider,
            id,
            name);
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
        DependsOnIfMissing(target);
        if (provider is not null)
        {
            DependsOnIfMissing(provider);
        }

        return SetObjectAttribute(
            VirtualNetworkResourceTypeProvider.Attributes.EndpointMappings,
            _endpointMappings);
    }

    private NetworkingEndpointReferenceValue AddNetworkEndpoint(
        string name,
        string protocol,
        string host,
        int? port,
        string exposure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(host);

        AddEndpoint(
            name,
            protocol,
            port,
            exposure);
        AddEndpointNetworkMapping(
            name,
            CreateEndpointAddress(protocol, host, port),
            exposure: exposure);

        return NetworkingEndpointReferenceValue.ForResource(
            EffectiveResourceId,
            name,
            VirtualNetworkResourceTypeProvider.ResourceTypeId,
            ProviderId);
    }

    private static string CreateEndpointMappingId(
        NetworkingEndpointReferenceValue source,
        NetworkingEndpointReferenceValue target)
    {
        source.Resource.TryGetResourceId(out var sourceResourceId);
        target.Resource.TryGetResourceId(out var targetResourceId);
        return $"{sourceResourceId}:endpoint-mapping:{source.EndpointName}:{targetResourceId}:{target.EndpointName}";
    }

    private void AddEndpointReferenceDependencies(
        params NetworkingEndpointReferenceValue[] references)
    {
        foreach (var reference in references)
        {
            if (!reference.Resource.TryGetResourceId(out var resourceId) ||
                string.Equals(resourceId, EffectiveResourceId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            DependsOnIfMissing(
                resourceId,
                reference.Resource.TypeId,
                reference.Resource.ProviderId);
        }
    }

    private void DependsOnIfMissing(IResourceDefinitionBuilder resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        DependsOnIfMissing(
            resource.EffectiveResourceId,
            resource.ResourceTypeId,
            resource.ResourceProviderId);
    }

    private void DependsOnIfMissing(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        if (Dependencies.Any(reference =>
            reference.TryGetDependsOnResourceId(out var dependencyId) &&
            string.Equals(dependencyId, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        DependsOn(
            resourceId,
            typeId,
            providerId);
    }

    private static string CreateEndpointAddress(
        string protocol,
        string host,
        int? port)
    {
        var normalizedHost = host.Trim();
        if (Uri.TryCreate(normalizedHost, UriKind.Absolute, out var uri))
        {
            if (port is null)
            {
                return normalizedHost;
            }

            var builder = new UriBuilder(uri)
            {
                Port = port.Value
            };
            return builder.Uri.ToString().TrimEnd('/');
        }

        var normalizedProtocol = protocol.Trim().ToLowerInvariant();
        return port is null
            ? $"{normalizedProtocol}://{normalizedHost}"
            : $"{normalizedProtocol}://{normalizedHost}:{port.Value}";
    }
}

public static class VirtualNetworkResourceDefinitionBuilderExtensions
{
    public static VirtualNetworkResourceDefinitionBuilder AddVirtualNetwork(
        this ResourceDefinitionGraphBuilder graph,
        string name,
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(graph);

        var builder = new VirtualNetworkResourceDefinitionBuilder(name);
        graph.Add(builder);
        if (isDefault)
        {
            builder.AsDefault();
        }

        return builder;
    }
}
