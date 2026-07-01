using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceInternetReachabilityProjection
{
    public const string Reachable = "reachable";
    public const string Inferred = "inferred";
    public const string HostNetworkResourceId = "network:host";

    public static IReadOnlyDictionary<string, string> CreateMap(
        IReadOnlyList<Resource> resources,
        bool includeImplicitHostNetwork = false)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var distinctResources = ResourceCollectionProjection.DistinctById(resources);
        var resourcesById = ResourceCollectionProjection.ToDictionaryById(distinctResources);

        foreach (var resource in distinctResources)
        {
            if (GetExplicitReachability(resource) is { } reachability)
            {
                result[resource.Id] = reachability;
            }
        }

        foreach (var resource in distinctResources)
        {
            if (result.ContainsKey(resource.Id))
            {
                continue;
            }

            if ((includeImplicitHostNetwork && HasHostNetworkBinding(resource)) ||
                HasReachableNetworkBinding(resource, result, resourcesById))
            {
                result[resource.Id] = Inferred;
            }
        }

        if (includeImplicitHostNetwork &&
            !result.ContainsKey(HostNetworkResourceId) &&
            distinctResources.Any(resource =>
                !string.Equals(resource.Id, HostNetworkResourceId, StringComparison.OrdinalIgnoreCase) &&
                HasHostNetworkBinding(resource)))
        {
            result[HostNetworkResourceId] = Inferred;
        }

        foreach (var network in distinctResources.Where(resource => result.ContainsKey(resource.Id)))
        {
            foreach (var mapping in network.ResourceEndpointMappings)
            {
                AddInferredIfVisible(result, resourcesById, mapping.Source.ResourceId);
                AddInferredIfVisible(result, resourcesById, mapping.Target.ResourceId);
            }
        }

        return result;
    }

    public static IReadOnlyList<string> GetHostNetworkConnectedResourceIds(IReadOnlyList<Resource> resources)
    {
        ArgumentNullException.ThrowIfNull(resources);

        return resources
            .GroupBy(resource => resource.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Where(HasHostNetworkBinding)
            .Select(resource => resource.Id)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void AddInferredIfVisible(
        IDictionary<string, string> reachability,
        IReadOnlyDictionary<string, Resource> resourcesById,
        string? resourceId)
    {
        if (!string.IsNullOrWhiteSpace(resourceId) &&
            resourcesById.ContainsKey(resourceId) &&
            !reachability.ContainsKey(resourceId))
        {
            reachability[resourceId] = Inferred;
        }
    }

    private static bool HasReachableNetworkBinding(
        Resource resource,
        IReadOnlyDictionary<string, string> reachability,
        IReadOnlyDictionary<string, Resource> resourcesById) =>
        resource.ResourceEndpointNetworkMappings.Any(mapping =>
            IsReachableNetwork(mapping.NetworkResourceId, reachability, resourcesById) ||
            IsReachableNetwork(mapping.ProviderResourceId, reachability, resourcesById));

    private static bool IsReachableNetwork(
        string? resourceId,
        IReadOnlyDictionary<string, string> reachability,
        IReadOnlyDictionary<string, Resource> resourcesById) =>
        !string.IsNullOrWhiteSpace(resourceId) &&
        resourcesById.TryGetValue(resourceId, out var resource) &&
        resource.ResourceClass == ResourceClass.Network &&
        reachability.ContainsKey(resource.Id);

    private static bool HasHostNetworkBinding(Resource resource) =>
        resource.Endpoints.Any(endpoint => endpoint.Exposure is ResourceExposureScope.Local or ResourceExposureScope.Public) ||
        resource.ResourceEndpointNetworkMappings.Any(mapping =>
            string.Equals(mapping.NetworkResourceId, HostNetworkResourceId, StringComparison.OrdinalIgnoreCase) ||
            IsLocalHostAddress(mapping.Address));

    private static bool IsLocalHostAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address) ||
            !Uri.TryCreate(address, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
    }

    private static string? GetExplicitReachability(Resource resource)
    {
        var explicitReachability = FirstNonEmpty(
            resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.InternetReachability),
            resource.ResourceAttributes.GetValueOrDefault(ResourceAttributeNames.NetworkInternetReachability));
        return explicitReachability is null
            ? null
            : Normalize(explicitReachability);
    }

    private static string? Normalize(string value) =>
        value.Trim() switch
        {
            var reachable when string.Equals(reachable, "reachable", StringComparison.OrdinalIgnoreCase) => Reachable,
            var verified when string.Equals(verified, "verified", StringComparison.OrdinalIgnoreCase) => Reachable,
            var yes when string.Equals(yes, "true", StringComparison.OrdinalIgnoreCase) => Reachable,
            var yes when string.Equals(yes, "yes", StringComparison.OrdinalIgnoreCase) => Reachable,
            var inferred when string.Equals(inferred, "inferred", StringComparison.OrdinalIgnoreCase) => Inferred,
            _ => null
        };

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
}
