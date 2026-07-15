using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal static class GeneratedResourceEndpointRelatedResourceIds
{
    public static IReadOnlyList<string> Create(
        Resource resource,
        IEnumerable<NetworkEndpointMappingRelationship> inboundNetworkMappings,
        IEnumerable<LoadBalancerRouteRelationship> inboundLoadBalancerRoutes,
        IEnumerable<string> mappingProviderIds,
        string? loadBalancerHostResourceId)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(inboundNetworkMappings);
        ArgumentNullException.ThrowIfNull(inboundLoadBalancerRoutes);
        ArgumentNullException.ThrowIfNull(mappingProviderIds);

        return inboundNetworkMappings
            .Select(mapping => mapping.NetworkResourceId)
            .Concat(inboundLoadBalancerRoutes.Select(route => route.LoadBalancerResourceId))
            .Concat(resource.ResourceEndpointMappings.SelectMany(GetMappingResourceIds))
            .Concat(resource.ResourceEndpointNetworkMappings.SelectMany(GetEndpointNetworkMappingResourceIds))
            .Concat(inboundNetworkMappings.SelectMany(mapping => GetMappingResourceIds(mapping.Mapping)))
            .Concat(resource.ResourceLoadBalancerRoutes.Select(route => route.Target.ResourceId))
            .Concat(inboundLoadBalancerRoutes.Select(route => route.Route.Target.ResourceId))
            .Concat(mappingProviderIds)
            .Append(loadBalancerHostResourceId)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Select(id => id!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string?> GetMappingResourceIds(
        ResourceEndpointMappingDefinition mapping)
    {
        yield return mapping.Source.ResourceId;
        yield return mapping.Target.ResourceId;
        yield return mapping.NetworkResourceId;
        yield return mapping.ProviderResourceId;
    }

    private static IEnumerable<string?> GetEndpointNetworkMappingResourceIds(
        ResourceEndpointNetworkMapping mapping)
    {
        yield return mapping.Target.ResourceId;
        yield return mapping.NetworkResourceId;
        yield return mapping.ProviderResourceId;
    }
}

internal sealed record NetworkEndpointMappingRelationship(
    string NetworkResourceId,
    ResourceEndpointMappingDefinition Mapping);

internal sealed record LoadBalancerRouteRelationship(
    string LoadBalancerResourceId,
    LoadBalancerRoute Route);
