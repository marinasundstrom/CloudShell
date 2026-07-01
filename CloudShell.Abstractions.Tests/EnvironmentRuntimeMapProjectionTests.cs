using System.Globalization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class EnvironmentRuntimeMapProjectionTests
{
    [Fact]
    public void Create_GroupsContainerAppAsManagedOrchestrationService()
    {
        var app = CreateContainerApp();
        var replicas = Enumerable.Range(1, 3)
            .Select(replica => CreateReplica(app, replica, 3))
            .ToArray();
        var serviceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(app.Id);
        var replicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceId, null);

        var map = EnvironmentRuntimeMapProjection.Create(
            [app, .. replicas],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.DoesNotContain(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == app.Id);

        var serviceNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Equal(app.Id, serviceNode.ResourceId);
        Assert.Equal(serviceId, serviceNode.ServiceId);
        Assert.Equal("/resources/application.container-app:api", serviceNode.DetailUrl);

        var serviceGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Equal(app.Id, serviceGroup.ResourceId);
        Assert.Equal(serviceId, serviceGroup.ServiceId);
        Assert.Equal("Container app", serviceGroup.BadgeLabel);
        Assert.Contains(serviceNode.Id, serviceGroup.NodeIds);

        var replicaGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.ReplicaGroup);
        Assert.Equal(replicaGroupId, replicaGroup.ReplicaGroupId);
        Assert.Equal(serviceGroup.Id, replicaGroup.ParentGroupId);

        var replicaNodes = map.Nodes
            .Where(node => node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Replica)
            .OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(3, replicaNodes.Length);
        Assert.All(replicaNodes, node =>
        {
            Assert.Equal(serviceId, node.ServiceId);
            Assert.Equal(replicaGroupId, node.ReplicaGroupId);
            Assert.Contains(node.Id, replicaGroup.NodeIds);
        });

        Assert.Contains(map.Links, link =>
            link.Source == serviceNode.Id &&
            link.Target == $"replica-group:{replicaGroupId}" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService &&
            link.Scope == EnvironmentRuntimeMapLinkScopes.Internal);
    }

    [Fact]
    public void Create_NestsRoutingBindingsInsideOwningServiceBoundary()
    {
        var app = CreateContainerApp();
        var loadBalancer = CreateLoadBalancer();
        var serviceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(app.Id);
        var replicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(serviceId, "rev-2");
        var deployment = CreateDeploymentWithRoutingBinding(
            app.Id,
            serviceId,
            replicaGroupId,
            loadBalancer.Id);
        var replicaGroup = new EnvironmentRuntimeMapReplicaGroup(
            app.Id,
            serviceId,
            replicaGroupId,
            "rev-2",
            RequestedSlots: 2,
            OccupiedSlots: 2,
            MaterializedReplicas: 2,
            RepairingCount: 0,
            RepairFailedCount: 0);

        var map = EnvironmentRuntimeMapProjection.Create(
            [app, loadBalancer],
            [deployment],
            [replicaGroup],
            new EnvironmentRuntimeMapProjectionOptions
            {
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var routingNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
        Assert.Equal(serviceId, routingNode.ServiceId);
        Assert.Equal(replicaGroupId, routingNode.ReplicaGroupId);
        Assert.Equal("rev-2", routingNode.RuntimeRevisionId);

        var serviceGroup = Assert.Single(map.Groups, group =>
            group.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Contains(routingNode.Id, serviceGroup.NodeIds);

        var serviceNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService);
        Assert.Contains(map.Links, link =>
            link.Source == serviceNode.Id &&
            link.Target == routingNode.Id &&
            link.Label == "routes" &&
            link.Scope == EnvironmentRuntimeMapLinkScopes.Internal);
        Assert.Contains(map.Links, link =>
            link.Source == routingNode.Id &&
            link.Target == $"replica-group:{replicaGroupId}" &&
            link.Label == "targets" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
        Assert.Contains(map.Links, link =>
            link.Source == $"resource:{loadBalancer.Id}" &&
            link.Target == routingNode.Id &&
            link.Label == "materializes" &&
            link.ArtifactKind == EnvironmentRuntimeArtifactKinds.RoutingBinding);
    }

    [Fact]
    public void Create_ExcludesNetworkTopologyOverlayByDefault()
    {
        var api = CreateHttpResource();
        var network = CreateNetworkResource(api.Id, providerResourceId: null);

        var map = EnvironmentRuntimeMapProjection.Create(
            [api, network],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.Empty(map.Nodes);
        Assert.Equal(0, map.NetworkTopologyCount);
    }

    [Fact]
    public void Create_IncludesNetworkResourcesAndMappingsWhenTopologyOverlayIsEnabled()
    {
        var api = CreateHttpResource();
        var provider = CreateTopologyProviderResource();
        var network = CreateNetworkResource(api.Id, provider.Id) with
        {
            Attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NetworkInternetReachability] = "verified"
            }
        };

        var map = EnvironmentRuntimeMapProjection.Create(
            [api, network, provider],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown",
                GetResourceTypeLabel = resource => resource.Id == api.Id
                    ? "Executable application"
                    : resource.EffectiveTypeId,
                GetResourceIconName = resource => resource.Id == api.Id
                    ? "application"
                    : resource.EffectiveTypeId
            });

        var networkNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == network.Id);
        var apiNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == api.Id);
        var providerNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == provider.Id);
        var mappingNode = Assert.Single(map.Nodes, node =>
            node.NodeKind == "topology" &&
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.EndpointMapping);
        var internetNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.InternetConnection);

        Assert.Equal("reachable", networkNode.InternetReachability);
        Assert.Equal("inferred", apiNode.InternetReachability);
        Assert.Equal("Executable application", apiNode.Type);
        Assert.Equal("application", apiNode.IconName);
        Assert.Equal("https://api.example.test", apiNode.EndpointText);
        Assert.Equal("public-http", mappingNode.Label);
        Assert.Equal("api.example.test", mappingNode.Type);
        Assert.Equal(3, map.NetworkTopologyCount);
        Assert.Contains(map.Links, link =>
            link.Source == networkNode.Id &&
            link.Target == mappingNode.Id &&
            link.Label == "contains" &&
            link.Kind == "topology");
        Assert.Contains(map.Links, link =>
            link.Source == providerNode.Id &&
            link.Target == mappingNode.Id &&
            link.Label == "materializes" &&
            link.Kind == "topology");
        Assert.Contains(map.Links, link =>
            link.Source == mappingNode.Id &&
            link.Target == apiNode.Id &&
            link.Label == "targets" &&
            link.Kind == "topology");
        Assert.Contains(map.Links, link =>
            link.Source == internetNode.Id &&
            link.Target == networkNode.Id &&
            link.Label == "reaches" &&
            link.Kind == "topology");
        Assert.DoesNotContain(map.Links, link =>
            link.Source == internetNode.Id &&
            link.Target == apiNode.Id);
    }

    [Fact]
    public void Create_DoesNotShowInternetConnectionForUnreachableNetworkTopology()
    {
        var api = CreatePrivateNetworkHttpResource();
        var provider = CreateTopologyProviderResource();
        var network = CreateNetworkResource(api.Id, provider.Id);

        var map = EnvironmentRuntimeMapProjection.Create(
            [api, network, provider],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var apiNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == api.Id);
        var networkNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.Resource &&
            node.ResourceId == network.Id);

        Assert.Null(networkNode.InternetReachability);
        Assert.Null(apiNode.InternetReachability);
        Assert.DoesNotContain(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.InternetConnection);
        Assert.DoesNotContain(map.Links, link => link.Label == "reaches");
    }

    [Fact]
    public void Create_ShowsInferredInternetConnectivityForLocalHostEndpointBinding()
    {
        var api = CreateLocalHostHttpResource();

        var map = EnvironmentRuntimeMapProjection.Create(
            [api],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var apiNode = Assert.Single(map.Nodes, node => node.ResourceId == api.Id);
        Assert.Equal("http://localhost:8080", apiNode.EndpointText);
        var hostNetworkNode = Assert.Single(map.Nodes, node =>
            node.ResourceId == "network:host" &&
            node.Label == "Host network" &&
            node.NodeKind == "topology");
        Assert.Equal("inferred", apiNode.InternetReachability);
        Assert.Equal("inferred", hostNetworkNode.InternetReachability);
        var internetNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.InternetConnection);
        Assert.Contains(map.Links, link =>
            link.Source == hostNetworkNode.Id &&
            link.Target == apiNode.Id &&
            link.Label == "connects" &&
            link.Kind == "topology");
        Assert.Contains(map.Links, link =>
            link.Source == internetNode.Id &&
            link.Target == hostNetworkNode.Id &&
            link.Label == "reaches" &&
            link.Kind == "topology");
        Assert.DoesNotContain(map.Links, link =>
            link.Source == internetNode.Id &&
            link.Target == apiNode.Id);
    }

    [Fact]
    public void Create_ConnectsExistingHostNetworkResourceToLocalHostEndpointBindings()
    {
        var api = CreateLocalHostHttpResource();
        var hostNetwork = CreateHostNetworkResource();

        var map = EnvironmentRuntimeMapProjection.Create(
            [api, hostNetwork],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var apiNode = Assert.Single(map.Nodes, node => node.ResourceId == api.Id);
        var hostNetworkNode = Assert.Single(map.Nodes, node =>
            node.ResourceId == "network:host" &&
            node.Label == "Host Network");
        var internetNode = Assert.Single(map.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.InternetConnection);

        Assert.Equal("inferred", hostNetworkNode.InternetReachability);
        Assert.Contains(map.Links, link =>
            link.Source == hostNetworkNode.Id &&
            link.Target == apiNode.Id &&
            link.Label == "connects" &&
            link.Kind == "topology");
        Assert.Contains(map.Links, link =>
            link.Source == internetNode.Id &&
            link.Target == hostNetworkNode.Id &&
            link.Label == "reaches" &&
            link.Kind == "topology");
    }

    [Fact]
    public void Create_HidesEndpointTextWhenNetworkTopologyOverlayIsDisabled()
    {
        var api = CreateLocalHostHttpResource();

        var map = EnvironmentRuntimeMapProjection.Create(
            [api],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = false,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        var apiNode = Assert.Single(map.Nodes, node => node.ResourceId == api.Id);
        Assert.Null(apiNode.EndpointText);
    }

    [Fact]
    public void Create_IncludesDnsZoneContainmentForNameMappingsInTopologyOverlay()
    {
        var frontend = CreateHttpResource();
        var dnsZone = CreateDnsZoneResource();
        var nameMapping = CreateNameMappingResource(dnsZone.Id, frontend.Id);

        var map = EnvironmentRuntimeMapProjection.Create(
            [frontend, dnsZone, nameMapping],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeNetworkTopologyOverlay = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.Contains(map.Links, link =>
            link.Source == $"resource:{dnsZone.Id}" &&
            link.Target == $"name-mapping:{nameMapping.Id}" &&
            link.Label == "contains" &&
            link.Kind == "routing");
        Assert.Contains(map.Links, link =>
            link.Source == $"name-mapping:{nameMapping.Id}" &&
            link.Target == $"resource:{frontend.Id}" &&
            link.Label == "names" &&
            link.Kind == "routing");
    }

    [Fact]
    public void Create_InfersSqlServerReferenceThroughDatabaseDependency()
    {
        var sqlServer = CreateSqlServerResource();
        var database = CreateSqlDatabaseResource(sqlServer.Id);
        var api = CreateHttpResource() with
        {
            State = ResourceState.Running,
            DependsOn = [database.Id]
        };

        var map = EnvironmentRuntimeMapProjection.Create(
            [api, database, sqlServer],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeDependencyRelationships = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.Contains(map.Links, link =>
            link.Source == $"resource:{api.Id}" &&
            link.Target == $"resource:{sqlServer.Id}" &&
            link.Label == "uses database on" &&
            link.Kind == "dependency");
    }

    [Fact]
    public void Create_ProjectsDependencyRelationshipsToContainerAppServiceNodeWhenEnabled()
    {
        var app = CreateContainerApp();
        var frontend = CreateHttpResource() with
        {
            Id = "application.executable:frontend",
            Name = "frontend",
            State = ResourceState.Running,
            DependsOn = [app.Id],
            DisplayName = "Frontend"
        };

        var mapWithoutDependencies = EnvironmentRuntimeMapProjection.Create(
            [frontend, app],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeDependencyRelationships = false,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });
        var mapWithDependencies = EnvironmentRuntimeMapProjection.Create(
            [frontend, app],
            [],
            [],
            new EnvironmentRuntimeMapProjectionOptions
            {
                IncludeDependencyRelationships = true,
                CreateResourceDetailUrl = resource => $"/resources/{resource.Id}",
                GetStateClass = state => state == ResourceState.Running ? "state-running" : "state-unknown"
            });

        Assert.DoesNotContain(mapWithoutDependencies.Links, link => link.Kind == "dependency");

        var serviceNode = Assert.Single(mapWithDependencies.Nodes, node =>
            node.ArtifactKind == EnvironmentRuntimeArtifactKinds.OrchestrationService &&
            node.ResourceId == app.Id);
        Assert.Contains(mapWithDependencies.Links, link =>
            link.Source == $"resource:{frontend.Id}" &&
            link.Target == serviceNode.Id &&
            link.Label == "depends on" &&
            link.Kind == "dependency");
    }

    private static Resource CreateContainerApp() =>
        new(
            "application.container-app:api",
            "api",
            "application.container-app",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.container-app",
            ResourceClass: ResourceClass.Service,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ContainerImage] = "api:latest",
                [ResourceAttributeNames.ContainerReplicas] = "3"
            },
            DisplayName: "Replicated API");

    private static Resource CreateLoadBalancer() =>
        new(
            "cloudshell.loadBalancer:public",
            "public",
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
            DisplayName: "Public load balancer");

    private static Resource CreateHttpResource() =>
        new(
            "application.executable:api",
            "api",
            "application.executable",
            "test",
            "local",
            null,
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

    private static Resource CreateHostNetworkResource() =>
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
            DisplayName: "Host Network");

    private static Resource CreatePrivateNetworkHttpResource() =>
        new(
            "application.executable:api",
            "api",
            "application.executable",
            "test",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Network, 8080)],
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
                    "http://api.internal:8080",
                    ResourceExposureScope.Network,
                    networkResourceId: "cloudshell.network:public",
                    providerResourceId: "cloudshell.gateway:public")
            ],
            DisplayName: "API");

    private static Resource CreateSqlServerResource() =>
        new(
            "application.sql-server:main",
            "main-sql",
            "application.sql-server",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "application.sql-server",
            ResourceClass: ResourceClass.Service,
            DisplayName: "Main SQL Server");

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

    private static Resource CreateDnsZoneResource() =>
        new(
            "dns:local",
            "local-dns",
            "cloudshell.dnsZone",
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.dnsZone",
            ResourceClass: ResourceClass.Network,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.DnsZoneName] = "application-topology.local"
            },
            Capabilities: [new(ResourceCapabilityIds.NetworkingDnsZone)],
            DisplayName: "Local DNS");

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

    private static Resource CreateTopologyProviderResource() =>
        new(
            "cloudshell.gateway:public",
            "public-gateway",
            "cloudshell.gateway",
            "test",
            "local",
            null,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            TypeId: "cloudshell.gateway",
            ResourceClass: ResourceClass.Network,
            DisplayName: "Public gateway");

    private static Resource CreateNetworkResource(string targetResourceId, string? providerResourceId) =>
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

    private static ResourceDeploymentRecord CreateDeploymentWithRoutingBinding(
        string resourceId,
        string serviceId,
        string replicaGroupId,
        string? loadBalancerResourceId = null)
    {
        var startedAt = new DateTimeOffset(2026, 6, 30, 10, 0, 0, TimeSpan.Zero);
        var routingBinding = new ResourceOrchestratorServiceRoutingBindingDefinition(
            "api-http-routing",
            ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
            serviceId,
            replicaGroupId,
            ResourceEndpointReference.ForEndpoint(resourceId, "http"),
            LoadBalancerResourceId: loadBalancerResourceId);
        return new ResourceDeploymentRecord(
            "api-deployment",
            "default",
            resourceId,
            serviceId,
            "rev-2",
            ResourceOrchestratorDeploymentStatus.Active,
            startedAt,
            CompletedAt: startedAt.AddSeconds(5),
            ReplicaGroup: new ResourceOrchestratorReplicaGroup(
                replicaGroupId,
                serviceId,
                "rev-2",
                2,
                [
                    new ResourceOrchestratorServiceInstance("api-rev-2-replica-1", 1, 2, "rev-2"),
                    new ResourceOrchestratorServiceInstance("api-rev-2-replica-2", 2, 2, "rev-2")
                ]),
            Definition: new ResourceOrchestratorDeploymentDefinition(
                ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                Services:
                [
                    new ResourceOrchestratorServiceDefinition(
                        serviceId,
                        ResourceOrchestratorDeploymentDefinitionTypes.Service,
                        ResourceOrchestratorDeploymentDefinition.CurrentDefinitionVersion,
                        Resources:
                        [
                            routingBinding.ToResourceDefinition()
                        ])
                ]));
    }

    private static Resource CreateReplica(Resource owner, int replica, int replicas)
    {
        var replicaName = $"api replica {replica.ToString(CultureInfo.InvariantCulture)}";
        return new Resource(
            $"runtime.container:api-{replica.ToString(CultureInfo.InvariantCulture)}",
            replicaName,
            "runtime.container",
            "test",
            "local",
            ResourceState.Running,
            [],
            "1",
            DateTimeOffset.UtcNow,
            [],
            ParentResourceId: owner.Id,
            TypeId: "runtime.container",
            ResourceClass: ResourceClass.Container,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.RuntimeKind] = "containerReplica",
                [ResourceAttributeNames.RuntimeContainerName] = replicaName,
                [ResourceAttributeNames.RuntimeReplicaOrdinal] = replica.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.RuntimeReplicaCount] = replicas.ToString(CultureInfo.InvariantCulture)
            },
            ManagementMode: ResourceManagementMode.RuntimeManaged,
            Visibility: ResourceVisibility.Hidden,
            OwnerResourceId: owner.Id,
            DisplayName: replicaName);
    }
}
