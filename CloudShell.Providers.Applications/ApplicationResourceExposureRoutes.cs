using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal static class ApplicationResourceExposureRoutes
{
    private const string NameMappingResourceType = "cloudshell.nameMapping";
    private const string LoadBalancerResourceType = "cloudshell.loadBalancer";

    public static string BuildAddNameMappingRoute(
        ApplicationResourceDefinition application,
        ResourceViewId returnViewId) =>
        BuildResourceAddRoute(BuildTargetResourceQuery(
            NameMappingResourceType,
            application.Id,
            GetTargetEndpoint(application)?.Name,
            returnViewId));

    public static string BuildAddNameMappingRoute(
        Resource resource,
        ResourceViewId returnViewId) =>
        BuildResourceAddRoute(BuildTargetResourceQuery(
            NameMappingResourceType,
            resource.Id,
            GetTargetEndpoint(resource)?.Name,
            returnViewId));

    public static string BuildAddLoadBalancerRoute(
        ApplicationResourceDefinition application,
        ResourceViewId returnViewId) =>
        BuildResourceAddRoute(BuildLoadBalancerQuery(
            application.Id,
            GetTargetEndpoint(application),
            returnViewId));

    public static string BuildAddLoadBalancerRoute(
        Resource resource,
        ResourceViewId returnViewId) =>
        BuildResourceAddRoute(BuildLoadBalancerQuery(
            resource.Id,
            GetTargetEndpoint(resource),
            returnViewId));

    public static string BuildUpdateLoadBalancerRoute(
        string loadBalancerResourceId,
        Resource resource,
        ResourceViewId returnViewId) =>
        BuildResourceUpdateRoute(
            loadBalancerResourceId,
            BuildLoadBalancerRouteQuery(
                resource.Id,
                GetTargetEndpoint(resource),
                returnViewId));

    private static Dictionary<string, string?> BuildLoadBalancerQuery(
        string resourceId,
        EndpointSelection? endpoint,
        ResourceViewId returnViewId)
    {
        var query = BuildTargetResourceQuery(
            LoadBalancerResourceType,
            resourceId,
            endpoint?.Name,
            returnViewId);
        if (string.Equals(endpoint?.Protocol, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            query["routeKind"] = "tcp";
        }

        return query;
    }

    private static Dictionary<string, string?> BuildLoadBalancerRouteQuery(
        string resourceId,
        EndpointSelection? endpoint,
        ResourceViewId returnViewId)
    {
        var query = new Dictionary<string, string?>
        {
            ["targetResourceId"] = resourceId,
            ["returnUrl"] = ResourceManagerRoutes.ResourceDetails(resourceId, returnViewId)
        };
        if (!string.IsNullOrWhiteSpace(endpoint?.Name))
        {
            query["targetEndpointName"] = endpoint.Name;
        }

        if (string.Equals(endpoint?.Protocol, "tcp", StringComparison.OrdinalIgnoreCase))
        {
            query["routeKind"] = "tcp";
        }

        return query;
    }

    private static Dictionary<string, string?> BuildTargetResourceQuery(
        string type,
        string resourceId,
        string? endpointName,
        ResourceViewId returnViewId)
    {
        var query = new Dictionary<string, string?>
        {
            ["type"] = type,
            ["targetResourceId"] = resourceId,
            ["returnUrl"] = ResourceManagerRoutes.ResourceDetails(resourceId, returnViewId)
        };
        if (!string.IsNullOrWhiteSpace(endpointName))
        {
            query["targetEndpointName"] = endpointName;
        }

        return query;
    }

    private static EndpointSelection? GetTargetEndpoint(ApplicationResourceDefinition application) =>
        application.EndpointPorts
            .Select(endpoint => new EndpointSelection(endpoint.Name, endpoint.Protocol))
            .OrderByDescending(endpoint => string.Equals(endpoint.Protocol, "http", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static EndpointSelection? GetTargetEndpoint(Resource resource) =>
        resource.Endpoints
            .Select(endpoint => new EndpointSelection(endpoint.Name, endpoint.Protocol))
            .OrderByDescending(endpoint => string.Equals(endpoint.Protocol, "http", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();

    private static string BuildResourceAddRoute(Dictionary<string, string?> query) =>
        BuildRoute("/resources/add", query);

    private static string BuildResourceUpdateRoute(string resourceId, Dictionary<string, string?> query) =>
        BuildRoute(
            ResourceManagerRoutes.ResourceDetails(resourceId, ResourcePredefinedViewIds.Configuration),
            query);

    private static string BuildRoute(string path, Dictionary<string, string?> query)
    {
        var queryString = string.Join(
            "&",
            query
                .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
                .Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!)}"));

        return string.IsNullOrWhiteSpace(queryString)
            ? path
            : $"{path}{(path.Contains('?', StringComparison.Ordinal) ? '&' : '?')}{queryString}";
    }

    private sealed record EndpointSelection(string Name, string Protocol);
}
