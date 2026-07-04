using System.Globalization;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;

namespace CloudShell.ControlPlane.Providers;

public sealed class LoadBalancerResourceManagerAttributeProvider :
    IResourceModelResourceManagerAttributeProvider
{
    public IReadOnlyDictionary<string, string>? GetAttributes(Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != LoadBalancerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var entrypoints = resource.State.ResourceAttributeValues
            .GetObject<LoadBalancerEntrypointValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Entrypoints) ?? [];
        var routes = resource.State.ResourceAttributeValues
            .GetObject<LoadBalancerRouteValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Routes) ?? [];

        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [LoadBalancerResourceTypeProvider.Attributes.EntrypointCount.ToString()] =
                entrypoints.Length.ToString(CultureInfo.InvariantCulture),
            [LoadBalancerResourceTypeProvider.Attributes.RouteCount.ToString()] =
                routes.Length.ToString(CultureInfo.InvariantCulture),
            [LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount.ToString()] =
                routes.Count(route =>
                    string.Equals(route.Kind, "Http", StringComparison.OrdinalIgnoreCase))
                    .ToString(CultureInfo.InvariantCulture),
            [LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount.ToString()] =
                routes.Count(route =>
                    string.Equals(route.Kind, "Tcp", StringComparison.OrdinalIgnoreCase))
                    .ToString(CultureInfo.InvariantCulture),
            [LoadBalancerResourceTypeProvider.Attributes.EndpointCount.ToString()] =
                entrypoints.Length.ToString(CultureInfo.InvariantCulture)
        };
    }
}

public sealed class LoadBalancerResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        Resource resource)
    {
        ArgumentNullException.ThrowIfNull(resource);

        if (resource.Type.TypeId != LoadBalancerResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var entrypoints = resource.Attributes
            .GetObject<LoadBalancerEntrypointValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Entrypoints) ?? [];
        var routes = resource.Attributes
            .GetObject<LoadBalancerRouteValue[]>(
                LoadBalancerResourceTypeProvider.Attributes.Routes) ?? [];

        return new ResourceModelResourceManagerEndpointProjection(
            Endpoints: entrypoints.Select(ToEndpoint).ToArray(),
            EndpointNetworkMappings: entrypoints
                .Select(entrypoint => ToEndpointNetworkMapping(resource, entrypoint))
                .ToArray(),
            LoadBalancerRoutes: routes
                .Select(ToLoadBalancerRoute)
                .Where(route => route is not null)
                .Cast<LoadBalancerRoute>()
                .ToArray());
    }

    private static ResourceEndpoint ToEndpoint(LoadBalancerEntrypointValue entrypoint)
    {
        var protocol = ParseProtocol(entrypoint.Protocol);
        return ResourceEndpoint.Contract(
            entrypoint.Name,
            protocol.ToString().ToLowerInvariant(),
            ParseExposure(entrypoint.Exposure),
            entrypoint.Port);
    }

    private static ResourceEndpointNetworkMapping ToEndpointNetworkMapping(
        Resource resource,
        LoadBalancerEntrypointValue entrypoint)
    {
        var protocol = ParseProtocol(entrypoint.Protocol);
        var scheme = protocol.ToString().ToLowerInvariant();
        var address = protocol switch
        {
            ResourceEndpointProtocol.Http => $"http://localhost:{entrypoint.Port}",
            ResourceEndpointProtocol.Https => $"https://localhost:{entrypoint.Port}",
            _ => $"{scheme}://localhost:{entrypoint.Port}"
        };

        return ResourceEndpointNetworkMapping.ForEndpoint(
            resource.EffectiveResourceId,
            entrypoint.Name,
            address,
            ParseExposure(entrypoint.Exposure),
            sourceEndpointName: entrypoint.Name);
    }

    private static LoadBalancerRoute? ToLoadBalancerRoute(
        LoadBalancerRouteValue value)
    {
        if (!value.Target.Resource.TryGetResourceId(out var targetResourceId))
        {
            return null;
        }

        return new(
            value.Id,
            value.Name,
            ParseRouteKind(value.Kind),
            value.EntrypointName,
            new LoadBalancerRouteMatch(
                value.Match.Host,
                value.Match.PathPrefix,
                value.Match.Port),
            new LoadBalancerRouteTarget(
                targetResourceId,
                value.Target.EndpointName,
                value.Target.Port));
    }

    private static ResourceEndpointProtocol ParseProtocol(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<ResourceEndpointProtocol>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceEndpointProtocol.Http;

    private static ResourceExposureScope ParseExposure(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<ResourceExposureScope>(value, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceExposureScope.Public;

    private static LoadBalancerRouteKind ParseRouteKind(string? value) =>
        !string.IsNullOrWhiteSpace(value) &&
        Enum.TryParse<LoadBalancerRouteKind>(value, ignoreCase: true, out var parsed)
            ? parsed
            : LoadBalancerRouteKind.Http;
}
