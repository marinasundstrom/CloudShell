using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDiagnosticDisplayTests
{
    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublisherResourceIsMissing()
    {
        var mapping = CreateNameMapping("networking:missing");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("DNS publisher unavailable", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'networking:missing' could not be found. CloudShell cannot verify that this name mapping can be published.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublisherDoesNotAdvertiseCapability()
    {
        var mapping = CreateNameMapping("networking:resolver");
        var provider = new Resource(
            "networking:resolver",
            "Resolver",
            "Network provider",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "resolver",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameResolver)]);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.Id] = provider
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("DNS publisher capability missing", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'Resolver' does not advertise the DNS name publisher capability.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenNameMappingPublisherAdvertisesCapability()
    {
        var mapping = CreateNameMapping("networking:publisher");
        var provider = new Resource(
            "networking:publisher",
            "Publisher",
            "Network provider",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "publisher",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: [new(ResourceCapabilityIds.NetworkingNamePublisher)]);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [provider.Id] = provider
            });

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenLoadBalancerHostResourceIsMissing()
    {
        var loadBalancer = CreateLoadBalancer("docker:missing");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            loadBalancer,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("Load balancer host unavailable", diagnostic.Title);
        Assert.Equal(
            "Container host resource 'docker:missing' could not be found. Provider-owned load balancer runtime may not be placeable.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnForDefaultLoadBalancerHostMarker()
    {
        var loadBalancer = CreateLoadBalancer("default");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            loadBalancer,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenLoadBalancerRouteTargetResourceIsMissing()
    {
        var loadBalancer = CreateLoadBalancer(
            hostResourceId: null,
            routes:
            [
                new LoadBalancerRoute(
                    "api",
                    "API",
                    LoadBalancerRouteKind.Http,
                    "web",
                    new LoadBalancerRouteMatch("api.local", "/"),
                    new LoadBalancerRouteTarget("application:api", "http"))
            ]);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            loadBalancer,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("Load balancer route target unavailable", diagnostic.Title);
        Assert.Equal(
            "Route 'API' targets resource 'application:api', but that resource could not be found.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenLoadBalancerRouteTargetEndpointIsMissing()
    {
        var loadBalancer = CreateLoadBalancer(
            hostResourceId: null,
            routes:
            [
                new LoadBalancerRoute(
                    "api",
                    "API",
                    LoadBalancerRouteKind.Http,
                    "web",
                    new LoadBalancerRouteMatch("api.local", "/"),
                    new LoadBalancerRouteTarget("application:api", "http"))
            ]);
        var target = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Http("admin", "api.local", 8081)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            ResourceClass: ResourceClass.Container);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            loadBalancer,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [target.Id] = target
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Warning", diagnostic.Severity);
        Assert.Equal("Load balancer route endpoint unavailable", diagnostic.Title);
        Assert.Equal(
            "Route 'API' targets endpoint 'http' on resource 'API', but that endpoint could not be found.",
            diagnostic.Message);
    }

    private static Resource CreateNameMapping(string providerResourceId) =>
        new(
            "dns:local:name:api-local",
            "api.local",
            "Name Mapping",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "api.local",
            DateTimeOffset.UtcNow,
            [providerResourceId],
            TypeId: PlatformResourceProvider.NameMappingResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingHostName] = "api.local",
                [ResourceAttributeNames.NameMappingTargetResourceId] = "application:api",
                [ResourceAttributeNames.NameMappingExposure] = ResourceExposureScope.Public.ToString(),
                [ResourceAttributeNames.NameMappingStatus] = "Ready",
                [ResourceAttributeNames.NameMappingMaterializationStatus] = "ProviderSelected",
                [ResourceAttributeNames.NameMappingProviderResourceId] = providerResourceId
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);

    private static Resource CreateLoadBalancer(
        string? hostResourceId,
        IReadOnlyList<LoadBalancerRoute>? routes = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.LoadBalancerProvider] = "traefik"
        };
        if (!string.IsNullOrWhiteSpace(hostResourceId))
        {
            attributes[ResourceAttributeNames.LoadBalancerHostResourceId] = hostResourceId;
        }

        return new Resource(
            "load-balancer:public",
            "Public",
            "Load Balancer",
            "CloudShell",
            "local",
            ResourceState.Stopped,
            [ResourceEndpoint.Http("web", "localhost", 8080)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.LoadBalancerResourceType,
            ResourceClass: ResourceClass.Network,
            Attributes: attributes,
            Capabilities: [new(ResourceCapabilityIds.NetworkingLoadBalancer)],
            LoadBalancerRoutes: routes);
    }
}
