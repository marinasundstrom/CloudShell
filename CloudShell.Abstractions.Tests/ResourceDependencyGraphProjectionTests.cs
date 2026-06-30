using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDependencyGraphProjectionTests
{
    [Fact]
    public void Create_ProjectsResourceDependenciesByDefault()
    {
        var settings = CreateResource("configuration:settings", "settings", ResourceClass.Configuration);
        var api = CreateResource(
            "application.executable:api",
            "api",
            ResourceClass.Executable,
            dependsOn: [settings.Id]);

        var graph = ResourceDependencyGraphProjection.Create(
            [api, settings],
            CreateOptions());

        Assert.Equal(2, graph.ResourceCount);
        Assert.Equal(1, graph.DependencyCount);
        Assert.Equal(0, graph.TopologyCount);
        Assert.Equal(2, graph.Nodes.Count);
        var dependency = Assert.Single(graph.Links);
        Assert.Equal(api.Id, dependency.Source);
        Assert.Equal(settings.Id, dependency.Target);
        Assert.Equal("depends on", dependency.Label);
        Assert.Equal(ResourceDependencyGraphLinkKinds.Dependency, dependency.Kind);
    }

    [Fact]
    public void Create_ExcludesNetworkTopologyOverlayByDefault()
    {
        var api = CreatePublicHttpResource();
        var network = CreateNetworkResource(api.Id, "cloudshell.gateway:public");

        var graph = ResourceDependencyGraphProjection.Create(
            [api, network],
            CreateOptions());

        Assert.Equal(2, graph.ResourceCount);
        Assert.Equal(0, graph.DependencyCount);
        Assert.Equal(0, graph.TopologyCount);
        Assert.Empty(graph.Links);
        Assert.All(graph.Nodes, node => Assert.Null(node.InternetReachability));
    }

    [Fact]
    public void Create_IncludesNetworkTopologyOverlayWhenEnabled()
    {
        var api = CreatePublicHttpResource();
        var network = CreateNetworkResource(api.Id, "cloudshell.gateway:public");
        var gateway = CreateResource("cloudshell.gateway:public", "public-gateway", ResourceClass.Network);
        var loadBalancer = CreateLoadBalancerResource(api.Id);

        var graph = ResourceDependencyGraphProjection.Create(
            [api, network, gateway, loadBalancer],
            CreateOptions(includeNetworkTopologyOverlay: true));

        Assert.Equal(4, graph.ResourceCount);
        Assert.Equal(3, graph.TopologyCount);
        var apiNode = Assert.Single(graph.Nodes, node => node.Id == api.Id);
        Assert.Null(apiNode.InternetReachability);
        Assert.Contains(graph.Links, link =>
            link.Source == network.Id &&
            link.Target == api.Id &&
            link.Label == "maps to" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
        Assert.Contains(graph.Links, link =>
            link.Source == gateway.Id &&
            link.Target == api.Id &&
            link.Label == "materializes" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
        Assert.Contains(graph.Links, link =>
            link.Source == loadBalancer.Id &&
            link.Target == api.Id &&
            link.Label == "routes to" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
        Assert.DoesNotContain(graph.Nodes, node => node.Id == "internet:public");
        Assert.DoesNotContain(graph.Links, link => link.Label == "reaches");
    }

    [Fact]
    public void Create_ShowsInternetReachabilityBadgeOnlyForProjectedReachability()
    {
        var localApi = CreatePublicHttpResource();
        var reachableNetwork = CreateNetworkResource(localApi.Id, "cloudshell.gateway:public") with
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NetworkInternetReachability] = "verified"
            }
        };
        var inferredGateway = CreateResource("cloudshell.gateway:public", "public-gateway", ResourceClass.Network) with
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.InternetReachability] = "inferred"
            }
        };

        var graph = ResourceDependencyGraphProjection.Create(
            [localApi, reachableNetwork, inferredGateway],
            CreateOptions(includeNetworkTopologyOverlay: true));

        Assert.Null(Assert.Single(graph.Nodes, node => node.Id == localApi.Id).InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Reachable,
            Assert.Single(graph.Nodes, node => node.Id == reachableNetwork.Id).InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Inferred,
            Assert.Single(graph.Nodes, node => node.Id == inferredGateway.Id).InternetReachability);
    }

    private static ResourceDependencyGraphProjectionOptions CreateOptions(
        bool includeNetworkTopologyOverlay = false) =>
        new()
        {
            IncludeNetworkTopologyOverlay = includeNetworkTopologyOverlay,
            CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
            GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
        };

    private static Resource CreateResource(
        string id,
        string name,
        ResourceClass resourceClass,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            id,
            name,
            resourceClass.ToString(),
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            TypeId: id.Split(':')[0],
            ResourceClass: resourceClass,
            DisplayName: name);

    private static Resource CreatePublicHttpResource() =>
        new(
            "application.executable:api",
            "api",
            "application.executable",
            "test",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Public, 8080)],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.executable",
            ResourceClass: ResourceClass.Executable,
            EndpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application.executable:api",
                    "http",
                    "https://api.example.test",
                    ResourceExposureScope.Public,
                    networkResourceId: "cloudshell.network:public",
                    providerResourceId: "cloudshell.gateway:public")
            ],
            DisplayName: "API");

    private static Resource CreateNetworkResource(string targetResourceId, string providerResourceId) =>
        new(
            "cloudshell.network:public",
            "public",
            "cloudshell.network",
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.network",
            ResourceClass: ResourceClass.Network,
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "public-http",
                    "public-http",
                    ResourceEndpointReference.ForEndpoint("cloudshell.network:public", "http"),
                    ResourceEndpointReference.ForEndpoint(targetResourceId, "http"),
                    NetworkResourceId: "cloudshell.network:public",
                    ProviderResourceId: providerResourceId)
            ],
            DisplayName: "Public network");

    private static Resource CreateLoadBalancerResource(string targetResourceId) =>
        new(
            "cloudshell.loadBalancer:public",
            "public-lb",
            "cloudshell.loadBalancer",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.loadBalancer",
            ResourceClass: ResourceClass.Network,
            LoadBalancerRoutes:
            [
                new LoadBalancerRoute(
                    "api-http",
                    "api-http",
                    LoadBalancerRouteKind.Http,
                    "http",
                    new LoadBalancerRouteMatch("api.example.test", "/"),
                    new LoadBalancerRouteTarget(targetResourceId, "http"))
            ],
            DisplayName: "Public load balancer");
}
