using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class GeneratedResourceEndpointRelatedResourceIdsTests
{
    [Fact]
    public void Create_IncludesEndpointNetworkMappingTopologyAndProviderResources()
    {
        var resource = CreateResource(
            "application.dotnet-app:api",
            endpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application.dotnet-app:api",
                    "http",
                    "http://localhost:5291",
                    ResourceExposureScope.Public,
                    networkResourceId: "cloudshell.virtualNetwork:sample-vnet",
                    providerResourceId: "cloudshell.hostNetworking.local:host-local")
            ]);

        var ids = GeneratedResourceEndpointRelatedResourceIds.Create(
            resource,
            [],
            [],
            [],
            loadBalancerHostResourceId: null);

        Assert.Contains("application.dotnet-app:api", ids);
        Assert.Contains("cloudshell.virtualNetwork:sample-vnet", ids);
        Assert.Contains("cloudshell.hostNetworking.local:host-local", ids);
    }

    [Fact]
    public void Create_IncludesInboundMappingAndLoadBalancerRelationships()
    {
        var resource = CreateResource(
            "application.dotnet-app:api",
            endpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api-public",
                    "API public ingress",
                    ResourceEndpointReference.ForEndpoint(
                        "cloudshell.virtualNetwork:sample-vnet",
                        "api-public"),
                    ResourceEndpointReference.ForEndpoint(
                        "application.dotnet-app:api",
                        "http"),
                    "cloudshell.virtualNetwork:sample-vnet",
                    "cloudshell.hostNetworking.local:host-local")
            ],
            loadBalancerRoutes:
            [
                new LoadBalancerRoute(
                    "route:api",
                    "API route",
                    LoadBalancerRouteKind.Http,
                    "http",
                    new LoadBalancerRouteMatch("api.cloudshell.local"),
                    new LoadBalancerRouteTarget("application.dotnet-app:api", "http"))
            ]);
        var inboundMapping = new NetworkEndpointMappingRelationship(
            "cloudshell.virtualNetwork:edge",
            new ResourceEndpointMappingDefinition(
                "mapping:inbound",
                "Inbound",
                ResourceEndpointReference.ForEndpoint(
                    "cloudshell.virtualNetwork:edge",
                    "public"),
                ResourceEndpointReference.ForEndpoint(
                    "application.dotnet-app:api",
                    "http"),
                "cloudshell.virtualNetwork:edge",
                "cloudshell.hostNetworking.local:edge"));
        var inboundRoute = new LoadBalancerRouteRelationship(
            "cloudshell.loadBalancer:public",
            new LoadBalancerRoute(
                "route:inbound",
                "Inbound route",
                LoadBalancerRouteKind.Http,
                "http",
                new LoadBalancerRouteMatch("api.example.test"),
                new LoadBalancerRouteTarget("application.dotnet-app:api", "http")));

        var ids = GeneratedResourceEndpointRelatedResourceIds.Create(
            resource,
            [inboundMapping],
            [inboundRoute],
            ["cloudshell.hostNetworking.local:configured"],
            "docker.host:sample");

        Assert.Contains("cloudshell.virtualNetwork:sample-vnet", ids);
        Assert.Contains("cloudshell.hostNetworking.local:host-local", ids);
        Assert.Contains("cloudshell.virtualNetwork:edge", ids);
        Assert.Contains("cloudshell.hostNetworking.local:edge", ids);
        Assert.Contains("cloudshell.loadBalancer:public", ids);
        Assert.Contains("cloudshell.hostNetworking.local:configured", ids);
        Assert.Contains("docker.host:sample", ids);
    }

    private static Resource CreateResource(
        string id,
        IReadOnlyList<ResourceEndpointMappingDefinition>? endpointMappings = null,
        IReadOnlyList<ResourceEndpointNetworkMapping>? endpointNetworkMappings = null,
        IReadOnlyList<LoadBalancerRoute>? loadBalancerRoutes = null) =>
        new(
            id,
            ResourceDisplayLabels.GetName(id),
            "application.dotnet-app",
            "resource-model",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Http("http", "localhost", 5291)],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.dotnet-app",
            ResourceClass: ResourceClass.Project,
            EndpointMappings: endpointMappings,
            EndpointNetworkMappings: endpointNetworkMappings,
            LoadBalancerRoutes: loadBalancerRoutes);
}
