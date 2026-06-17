using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.AspNetCore.WebUtilities;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourceExposureRoutesTests
{
    [Fact]
    public void BuildAddLoadBalancerRoute_AddsTcpRouteKindForTcpEndpoint()
    {
        var application = new ApplicationResourceDefinition(
            "application:orders-api",
            "orders-api",
            executablePath: string.Empty,
            endpointPorts:
            [
                new ServicePort("tds", 1433, Protocol: "tcp")
            ]);
        var returnView = new ResourceViewId(ResourceTabGroupIds.Runtime, "scaling");

        var route = ApplicationResourceExposureRoutes.BuildAddLoadBalancerRoute(
            application,
            returnView);

        var query = ParseQuery(route);
        Assert.Equal("/resources/add", GetPath(route));
        Assert.Equal("cloudshell.loadBalancer", query["type"]);
        Assert.Equal("application:orders-api", query["targetResourceId"]);
        Assert.Equal("tds", query["targetEndpointName"]);
        Assert.Equal("tcp", query["routeKind"]);
        Assert.Equal(
            ResourceManagerRoutes.ResourceDetails("application:orders-api", returnView),
            query["returnUrl"]);
    }

    [Fact]
    public void BuildAddNameMappingRoute_PrefersHttpEndpoint()
    {
        var application = new ApplicationResourceDefinition(
            "application:frontend",
            "frontend",
            executablePath: string.Empty,
            endpointPorts:
            [
                new ServicePort("metrics", 9090, Protocol: "tcp"),
                new ServicePort("http", 8080, Protocol: "http")
            ]);

        var route = ApplicationResourceExposureRoutes.BuildAddNameMappingRoute(
            application,
            ResourcePredefinedViewIds.Overview);

        var query = ParseQuery(route);
        Assert.Equal("cloudshell.nameMapping", query["type"]);
        Assert.Equal("application:frontend", query["targetResourceId"]);
        Assert.Equal("http", query["targetEndpointName"]);
        Assert.Equal(
            ResourceManagerRoutes.ResourceOverview("application:frontend"),
            query["returnUrl"]);
    }

    [Fact]
    public void BuildAddLoadBalancerRoute_UsesResourceEndpointShape()
    {
        var resource = new Resource(
            "application:worker-api",
            "worker-api",
            ApplicationResourceTypes.ContainerApp,
            "cloudshell.applications",
            "local",
            ResourceState.Stopped,
            [
                ResourceEndpoint.Tcp("tds", "localhost", 1433)
            ],
            "1",
            DateTimeOffset.UnixEpoch,
            []);

        var route = ApplicationResourceExposureRoutes.BuildAddLoadBalancerRoute(
            resource,
            ResourcePredefinedViewIds.Endpoints);

        var query = ParseQuery(route);
        Assert.Equal("application:worker-api", query["targetResourceId"]);
        Assert.Equal("tds", query["targetEndpointName"]);
        Assert.Equal("tcp", query["routeKind"]);
        Assert.Equal(
            ResourceManagerRoutes.ResourceDetails("application:worker-api", ResourcePredefinedViewIds.Endpoints),
            query["returnUrl"]);
    }

    private static string GetPath(string route)
    {
        var separatorIndex = route.IndexOf('?', StringComparison.Ordinal);
        return separatorIndex < 0 ? route : route[..separatorIndex];
    }

    private static Dictionary<string, string> ParseQuery(string route)
    {
        var separatorIndex = route.IndexOf('?', StringComparison.Ordinal);
        var query = separatorIndex < 0 ? string.Empty : route[separatorIndex..];

        return QueryHelpers.ParseQuery(query)
            .ToDictionary(
                parameter => parameter.Key,
                parameter => parameter.Value.ToString(),
                StringComparer.OrdinalIgnoreCase);
    }
}
