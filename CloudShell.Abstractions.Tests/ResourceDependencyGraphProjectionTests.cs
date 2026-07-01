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
    public void Create_UsesConfiguredResourceTypeLabels()
    {
        var api = CreateResource("application.executable:api", "api", ResourceClass.Executable);

        var graph = ResourceDependencyGraphProjection.Create(
            [api],
            CreateOptions(
                getResourceTypeLabel: _ => "Executable application",
                getResourceIconName: _ => "application"));

        var apiNode = Assert.Single(graph.Nodes);
        Assert.Equal("Executable application", apiNode.Type);
        Assert.Equal("application", apiNode.IconName);
    }

    [Fact]
    public void Create_ExcludesNetworkTopologyOverlayAndConnectivityBadgesByDefault()
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
        Assert.All(graph.Nodes, node => Assert.Null(node.EndpointText));
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
        Assert.Equal("https://api.example.test", apiNode.EndpointText);
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
    public void Create_IncludesDnsZoneContainmentForNameMappingsInTopologyOverlay()
    {
        var frontend = CreatePublicHttpResource();
        var dnsZone = CreateResource("dns:local", "local-dns", ResourceClass.Network);
        var nameMapping = CreateNameMappingResource(dnsZone.Id, frontend.Id);

        var graph = ResourceDependencyGraphProjection.Create(
            [frontend, dnsZone, nameMapping],
            CreateOptions(includeNetworkTopologyOverlay: true));

        var frontendNode = Assert.Single(graph.Nodes, node => node.Id == frontend.Id);
        Assert.Equal("app.local", frontendNode.EndpointText);
        Assert.Contains(graph.Links, link =>
            link.Source == dnsZone.Id &&
            link.Target == nameMapping.Id &&
            link.Label == "contains" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
        Assert.Contains(graph.Links, link =>
            link.Source == nameMapping.Id &&
            link.Target == frontend.Id &&
            link.Label == "names" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
    }

    [Fact]
    public void Create_InfersSqlServerReferenceThroughDatabaseDependency()
    {
        var sqlServer = CreateResource("application.sql-server:main", "main-sql", ResourceClass.Service);
        var database = CreateSqlDatabaseResource(sqlServer.Id);
        var api = CreateResource(
            "application.executable:api",
            "api",
            ResourceClass.Executable,
            dependsOn: [database.Id]);

        var graph = ResourceDependencyGraphProjection.Create(
            [api, sqlServer],
            CreateOptions(relationshipResources: [api, database, sqlServer]));

        Assert.Contains(graph.Links, link =>
            link.Source == api.Id &&
            link.Target == sqlServer.Id &&
            link.Label == "uses database on" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Dependency);
        Assert.DoesNotContain(graph.Links, link => link.Source == api.Id && link.Target == database.Id);
    }

    [Fact]
    public void Create_ShowsDatabaseServerPathWhenDatabaseIsVisible()
    {
        var sqlServer = CreateResource("application.sql-server:main", "main-sql", ResourceClass.Service);
        var database = CreateSqlDatabaseResource(sqlServer.Id);
        var api = CreateResource(
            "application.executable:api",
            "api",
            ResourceClass.Executable,
            dependsOn: [database.Id]);

        var graph = ResourceDependencyGraphProjection.Create(
            [api, database, sqlServer],
            CreateOptions());

        Assert.Contains(graph.Links, link =>
            link.Source == api.Id &&
            link.Target == database.Id &&
            link.Label == "depends on" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Dependency);
        Assert.Contains(graph.Links, link =>
            link.Source == database.Id &&
            link.Target == sqlServer.Id &&
            link.Label == "hosted by" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Dependency);
        Assert.DoesNotContain(graph.Links, link =>
            link.Source == api.Id &&
            link.Target == sqlServer.Id &&
            link.Label == "uses database on");
    }

    [Fact]
    public void Create_ShowsInternetReachabilityBadgeFromProjectedAndInferredConnectivity()
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

        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Inferred,
            Assert.Single(graph.Nodes, node => node.Id == localApi.Id).InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Reachable,
            Assert.Single(graph.Nodes, node => node.Id == reachableNetwork.Id).InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Inferred,
            Assert.Single(graph.Nodes, node => node.Id == inferredGateway.Id).InternetReachability);
    }

    [Fact]
    public void Create_HidesImplicitHostNetworkConnectivityByDefault()
    {
        var localApi = CreateLocalHostHttpResource();

        var graph = ResourceDependencyGraphProjection.Create(
            [localApi],
            CreateOptions(includeNetworkTopologyOverlay: true));

        Assert.Equal(1, graph.ResourceCount);
        Assert.DoesNotContain(graph.Nodes, node => node.Id == "network:host");
        Assert.Null(Assert.Single(graph.Nodes, node => node.Id == localApi.Id).InternetReachability);
    }

    [Fact]
    public void Create_ProjectsImplicitHostNetworkWithoutTopologyOverlayWhenImplicitResourcesAreIncluded()
    {
        var localApi = CreateLocalHostHttpResource();

        var graph = ResourceDependencyGraphProjection.Create(
            [localApi],
            CreateOptions(includeImplicitResources: true));

        Assert.Equal(2, graph.ResourceCount);
        Assert.Equal(0, graph.TopologyCount);
        Assert.Contains(graph.Nodes, node =>
            node.Id == "network:host" &&
            node.Label == "Host network" &&
            node.EndpointText is null);
        Assert.Empty(graph.Links);
    }

    [Fact]
    public void Create_ProjectsImplicitHostNetworkWhenImplicitResourcesAreIncluded()
    {
        var localApi = CreateLocalHostHttpResource();

        var graph = ResourceDependencyGraphProjection.Create(
            [localApi],
            CreateOptions(
                includeNetworkTopologyOverlay: true,
                includeImplicitResources: true));

        Assert.Equal(2, graph.ResourceCount);
        Assert.Equal(1, graph.TopologyCount);
        var hostNetwork = Assert.Single(graph.Nodes, node => node.Id == "network:host");
        Assert.Equal("Host network", hostNetwork.Label);
        Assert.Equal("Network", hostNetwork.Type);
        Assert.Equal(ResourceDependencyGraphInternetReachability.Inferred, hostNetwork.InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Inferred,
            Assert.Single(graph.Nodes, node => node.Id == localApi.Id).InternetReachability);
        Assert.Contains(graph.Links, link =>
            link.Source == "network:host" &&
            link.Target == localApi.Id &&
            link.Label == "connects" &&
            link.Kind == ResourceDependencyGraphLinkKinds.Topology);
    }

    [Fact]
    public void Create_InfersInternetConnectivityThroughReachableNetwork()
    {
        var api = CreatePublicHttpResource();
        var network = CreateNetworkResource(api.Id, "cloudshell.gateway:public") with
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NetworkInternetReachability] = "verified"
            }
        };

        var graph = ResourceDependencyGraphProjection.Create(
            [api, network],
            CreateOptions(includeNetworkTopologyOverlay: true));

        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Inferred,
            Assert.Single(graph.Nodes, node => node.Id == api.Id).InternetReachability);
        Assert.Equal(
            ResourceDependencyGraphInternetReachability.Reachable,
            Assert.Single(graph.Nodes, node => node.Id == network.Id).InternetReachability);
    }

    [Fact]
    public void Create_RendersImplicitDefaultResourcesWhenTheyAreIncluded()
    {
        var hostNetwork = CreateHostNetworkResource();
        var defaultContainerHost = CreateDefaultContainerHostResource();

        var graph = ResourceDependencyGraphProjection.Create(
            [hostNetwork, defaultContainerHost],
            CreateOptions());

        Assert.Equal(2, graph.ResourceCount);
        Assert.Contains(graph.Nodes, node => node.Id == hostNetwork.Id);
        Assert.Contains(graph.Nodes, node => node.Id == defaultContainerHost.Id);
    }

    [Fact]
    public void IsImplicitDefaultResource_IdentifiesOnlyProjectedDefaultResources()
    {
        var hostNetwork = CreateHostNetworkResource();
        var defaultContainerHost = CreateDefaultContainerHostResource();
        var defaultDockerCompatibleHost = CreateDefaultDockerHostResource();
        var explicitContainerHost = CreateContainerHostResource(
            "cloudshell.container-host:remote",
            isDefault: false);
        var declaredHostNetwork = CreateHostNetworkResource(declared: true);
        var declaredDefaultContainerHost = CreateContainerHostResource(
            "cloudshell.container-host:override",
            isDefault: true,
            declared: true);
        var declaredDefaultDockerCompatibleHost = CreateDockerHostResource(
            "docker.host:override",
            isDefault: true,
            declared: true);

        Assert.True(ResourceGraphVisibility.IsImplicitDefaultResource(hostNetwork));
        Assert.True(ResourceGraphVisibility.IsImplicitDefaultResource(defaultContainerHost));
        Assert.True(ResourceGraphVisibility.IsImplicitDefaultResource(defaultDockerCompatibleHost));
        Assert.False(ResourceGraphVisibility.IsImplicitDefaultResource(explicitContainerHost));
        Assert.False(ResourceGraphVisibility.IsImplicitDefaultResource(declaredHostNetwork));
        Assert.False(ResourceGraphVisibility.IsImplicitDefaultResource(declaredDefaultContainerHost));
        Assert.False(ResourceGraphVisibility.IsImplicitDefaultResource(declaredDefaultDockerCompatibleHost));
    }

    private static ResourceDependencyGraphProjectionOptions CreateOptions(
        bool includeNetworkTopologyOverlay = false,
        bool includeImplicitResources = false,
        IReadOnlyList<Resource>? relationshipResources = null,
        Func<Resource, string>? getResourceTypeLabel = null,
        Func<Resource, string?>? getResourceIconName = null) =>
        new()
        {
            IncludeNetworkTopologyOverlay = includeNetworkTopologyOverlay,
            IncludeImplicitResources = includeImplicitResources,
            RelationshipResources = relationshipResources,
            GetResourceTypeLabel = getResourceTypeLabel ?? (resource => resource.EffectiveTypeId),
            GetResourceIconName = getResourceIconName ?? (resource => resource.EffectiveTypeId),
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

    private static Resource CreateHostNetworkResource(bool declared = false) =>
        new(
            "network:host",
            "host-network",
            "cloudshell.network",
            "test",
            "local",
            ResourceState.Running,
            [],
            "host default",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.network",
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ResourceGraphMembership] = declared
                    ? ResourceGraphMembershipKinds.Declared
                    : ResourceGraphMembershipKinds.Projected,
                [ResourceAttributeNames.NetworkKind] = "Host"
            },
            DisplayName: "Host network");

    private static Resource CreateDefaultDockerHostResource() =>
        CreateDockerHostResource("docker.host:local", isDefault: true);

    private static Resource CreateDefaultContainerHostResource() =>
        CreateContainerHostResource("cloudshell.container-host:local", isDefault: true);

    private static Resource CreateContainerHostResource(
        string id,
        bool isDefault,
        bool declared = false) =>
        CreateContainerHostResource(
            id,
            "cloudshell.container-host",
            "container.host.default",
            isDefault,
            declared,
            isDefault ? "Default container host" : "Remote container host");

    private static Resource CreateDockerHostResource(
        string id,
        bool isDefault,
        bool declared = false) =>
        CreateContainerHostResource(
            id,
            "docker.host",
            "docker.host.default",
            isDefault,
            declared,
            isDefault ? "Default Docker-compatible host" : "Remote Docker-compatible host");

    private static Resource CreateContainerHostResource(
        string id,
        string typeId,
        string defaultAttribute,
        bool isDefault,
        bool declared,
        string displayName) =>
        new(
            id,
            id.Split(':')[1],
            typeId,
            "test",
            "local",
            ResourceState.Running,
            [],
            "Docker",
            DateTimeOffset.UtcNow,
            [],
            TypeId: typeId,
            ResourceClass: ResourceClass.Infrastructure,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ResourceGraphMembership] = declared
                    ? ResourceGraphMembershipKinds.Declared
                    : ResourceGraphMembershipKinds.Projected,
                [defaultAttribute] = isDefault.ToString()
            },
            DisplayName: displayName);

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

    private static Resource CreateLocalHostHttpResource() =>
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
                    "http://localhost:8080",
                    ResourceExposureScope.Public,
                    networkResourceId: "network:host")
            ],
            DisplayName: "API");

    private static Resource CreateSqlDatabaseResource(string serverResourceId) =>
        new(
            "application.sql-database:app",
            "app-db",
            "application.sql-database",
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [serverResourceId],
            TypeId: "application.sql-database",
            ResourceClass: ResourceClass.Service,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.DatabaseServerResourceId] = serverResourceId,
                ["database.name"] = "app"
            },
            DisplayName: "App database");

    private static Resource CreateNameMappingResource(string zoneResourceId, string targetResourceId) =>
        new(
            "dns:local:name:app",
            "app-local",
            "cloudshell.nameMapping",
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: zoneResourceId,
            TypeId: "cloudshell.nameMapping",
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingHostName] = "app.local",
                [ResourceAttributeNames.NameMappingTargetResourceId] = targetResourceId,
                [ResourceAttributeNames.NameMappingTargetEndpointName] = "http",
                [ResourceAttributeNames.NameMappingExposure] = ResourceExposureScope.Public.ToString()
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)],
            DisplayName: "app.local");

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
