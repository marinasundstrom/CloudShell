using System.Globalization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceDiagnosticDisplayTests
{
    [Fact]
    public void GetDiagnostics_WarnsWhenStartPreflightHasUnavailableReason()
    {
        var resource = CreateLifecycleResource(ResourceState.Stopped);
        var capabilities = new ResourceOperationCapabilities(
            resource.Id,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [
                new ResourceActionCapability(
                    ResourceActionIds.Start,
                    false,
                    "Container host 'docker:missing' is not registered.")
            ]);

        var diagnostics = ResourceActionReadinessDiagnostics.GetDiagnostics(resource, capabilities);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Start readiness", diagnostic.Title);
        Assert.Equal("Container host 'docker:missing' is not registered.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_TrimsCurrentResourcePrefixFromReadinessReason()
    {
        var resource = CreateLifecycleResource(ResourceState.Stopped);
        var capabilities = new ResourceOperationCapabilities(
            resource.Id,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [
                new ResourceActionCapability(
                    ResourceActionIds.Start,
                    false,
                    "Project-backed application resource 'API' cannot start because project path 'missing.csproj' was not found.")
            ]);

        var diagnostics = ResourceActionReadinessDiagnostics.GetDiagnostics(resource, capabilities);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Cannot start because project path 'missing.csproj' was not found.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_TrimsCurrentResourceDisplayNamePrefixFromReadinessReason()
    {
        var resource = CreateLifecycleResource(
            ResourceState.Stopped,
            displayName: "Application API");
        var capabilities = new ResourceOperationCapabilities(
            resource.Id,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [
                new ResourceActionCapability(
                    ResourceActionIds.Start,
                    false,
                    "Application resource 'Application API' does not declare an executable path.")
            ]);

        var diagnostics = ResourceActionReadinessDiagnostics.GetDiagnostics(resource, capabilities);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Does not declare an executable path.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_UsesRestartReadinessForRunningResources()
    {
        var resource = CreateLifecycleResource(ResourceState.Running);
        var capabilities = new ResourceOperationCapabilities(
            resource.Id,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            [
                new ResourceActionCapability(
                    ResourceActionIds.Start,
                    false,
                    "Resource is already running."),
                new ResourceActionCapability(
                    ResourceActionIds.Restart,
                    false,
                    "Container host 'docker:missing' is not registered.")
            ]);

        var diagnostics = ResourceActionReadinessDiagnostics.GetDiagnostics(resource, capabilities);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal("Restart readiness", diagnostic.Title);
        Assert.Equal("Container host 'docker:missing' is not registered.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenReadinessActionCanExecute()
    {
        var resource = CreateLifecycleResource(ResourceState.Stopped);
        var capabilities = new ResourceOperationCapabilities(
            resource.Id,
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ResourceActionIds.Start
            });

        var diagnostics = ResourceActionReadinessDiagnostics.GetDiagnostics(resource, capabilities);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void HasStateHealthIncongruity_ReturnsTrueForStoppedHealthyResource()
    {
        var resource = CreateLifecycleResource(
            ResourceState.Stopped,
            healthChecks: [new ResourceHealthCheck("/health")]);
        var health = new ResourceHealthSummary(
            resource.Id,
            ResourceHealthStatus.Healthy,
            DateTimeOffset.UtcNow,
            []);

        Assert.True(ResourceLifecycleDisplay.HasStateHealthIncongruity(resource, health));
    }

    [Fact]
    public void HasStateHealthIncongruity_ReturnsFalseForRunningHealthyResource()
    {
        var resource = CreateLifecycleResource(
            ResourceState.Running,
            healthChecks: [new ResourceHealthCheck("/health")]);
        var health = new ResourceHealthSummary(
            resource.Id,
            ResourceHealthStatus.Healthy,
            DateTimeOffset.UtcNow,
            []);

        Assert.False(ResourceLifecycleDisplay.HasStateHealthIncongruity(resource, health));
    }

    [Fact]
    public void HasStateHealthIncongruity_ReturnsFalseWhenStoppedHealthIsUnhealthy()
    {
        var resource = CreateLifecycleResource(
            ResourceState.Stopped,
            healthChecks: [new ResourceHealthCheck("/health")]);
        var health = new ResourceHealthSummary(
            resource.Id,
            ResourceHealthStatus.Unhealthy,
            DateTimeOffset.UtcNow,
            []);

        Assert.False(ResourceLifecycleDisplay.HasStateHealthIncongruity(resource, health));
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublisherResourceIsMissing()
    {
        var mapping = CreateNameMapping("networking:missing");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            CreateNameMappingRelatedResources());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("DNS publisher unavailable", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'missing' could not be found. CloudShell cannot verify that this name mapping can be published.",
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
            CreateNameMappingRelatedResources(provider));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("DNS publisher capability missing", diagnostic.Title);
        Assert.Equal(
            "Provider resource 'Resolver' does not advertise the DNS name publisher capability.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_ShowsPublisherResourceNameMappingAsPendingPublishWhenPublisherAdvertisesCapability()
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
            CreateNameMappingRelatedResources(provider));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Info, diagnostic.Severity);
        Assert.Equal("Name mapping pending publish", diagnostic.Title);
        Assert.Equal(
            "Provider selected. Run Reconcile name mappings on the DNS zone to apply it.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_ShowsProviderSelectedNameMappingAsPendingPublish()
    {
        var mapping = CreateNameMapping(
            string.Empty,
            materializationStatusReason: "DNS provider 'local-hostnames' is responsible for publishing this name.");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            CreateNameMappingRelatedResources());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Info, diagnostic.Severity);
        Assert.Equal("Name mapping pending publish", diagnostic.Title);
        Assert.Equal(
            "DNS provider 'local-hostnames' is responsible for publishing this name. Run Reconcile name mappings on the DNS zone to apply it.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_ShowsLocalHostNamePublishingDiagnostics()
    {
        var mapping = CreateNameMapping(
            string.Empty,
            materializationStatus: "Published",
            materializationStatusReason: "Published to custom hosts-file target.",
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingHostName] = "api.local",
                [ResourceAttributeNames.NameMappingLocalHostNamesHostsFilePath] = "/tmp/cloudshell-hosts",
                [ResourceAttributeNames.NameMappingLocalHostNamesHostsFileTarget] = "Custom",
                [ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshStatus] = "Skipped",
                [ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshReason] =
                    "Resolver cache was not refreshed because a custom hosts-file target is configured."
            });

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            CreateNameMappingRelatedResources());

        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceSignalSeverity.Info &&
            diagnostic.Title == "Local host-name target" &&
            diagnostic.Message == "Published to custom hosts-file target '/tmp/cloudshell-hosts'.");
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceSignalSeverity.Info &&
            diagnostic.Title == "Resolver cache not refreshed" &&
            diagnostic.Message.Contains("custom hosts-file target", StringComparison.Ordinal));
        Assert.Contains(diagnostics, diagnostic =>
            diagnostic.Severity == ResourceSignalSeverity.Warning &&
            diagnostic.Title == "Local suffix warning" &&
            diagnostic.Message.Contains("mDNS/Bonjour", StringComparison.Ordinal));
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingPublishFailed()
    {
        var mapping = CreateNameMapping(
            "networking:publisher",
            materializationStatus: "PublishFailed",
            materializationStatusReason: "Could not update hosts file.");
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
            CreateNameMappingRelatedResources(provider));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Name mapping publish failed", diagnostic.Title);
        Assert.Equal("Could not update hosts file.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenNameMappingIsPublished()
    {
        var mapping = CreateNameMapping(
            "networking:publisher",
            materializationStatus: "Published");
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
            CreateNameMappingRelatedResources(provider));

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingTargetResourceIsMissing()
    {
        var mapping = CreateNameMapping(string.Empty);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Name mapping target unavailable", diagnostic.Title);
        Assert.Equal("Target resource 'api' could not be found.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenNameMappingTargetEndpointIsMissing()
    {
        var mapping = CreateNameMapping(
            string.Empty,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.NameMappingTargetEndpointName] = "http"
            });
        var target = CreateEndpointResource(
            "application:api",
            "API",
            ResourceEndpoint.Http("admin", "api.local", 8081));

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            CreateNameMappingRelatedResources(target));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Name mapping target endpoint unavailable", diagnostic.Title);
        Assert.Equal("Target endpoint 'http' could not be found on resource 'API'.", diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenLocalHostNameMappingTargetEndpointHasNoMappedAddress()
    {
        var mapping = CreateNameMapping(
            string.Empty,
            attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.DnsProvider] = "local-hostnames",
                [ResourceAttributeNames.NameMappingTargetEndpointName] = "http"
            });

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            mapping,
            CreateNameMappingRelatedResources());

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Name mapping target address unavailable", diagnostic.Title);
        Assert.Equal(
            "Target endpoint 'http' on resource 'API' does not have a mapped address for local host-name publishing.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenEndpointMappingProviderResourceIsMissing()
    {
        var network = CreateNetworkWithEndpointMapping(providerResourceId: "networking:missing");
        var api = CreateEndpointResource("application:api", "API", ResourceEndpoint.Http("http", "api.local", 8080));

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            network,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [api.Id] = api
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Endpoint mapping provider unavailable", diagnostic.Title);
        Assert.Equal(
            "Mapping 'API' requires provider resource 'missing', but that resource could not be found.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenEndpointMappingProviderDoesNotAdvertiseCapability()
    {
        var network = CreateNetworkWithEndpointMapping(providerResourceId: "networking:proxy");
        var api = CreateEndpointResource("application:api", "API", ResourceEndpoint.Http("http", "api.local", 8080));
        var proxy = CreateEndpointResource("networking:proxy", "Proxy", ResourceEndpoint.Http("proxy", "proxy.local", 8081));

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            network,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [api.Id] = api,
                [proxy.Id] = proxy
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Endpoint mapping provider capability missing", diagnostic.Title);
        Assert.Equal(
            "Mapping 'API' provider resource 'Proxy' does not advertise the endpoint mapper capability.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenEndpointMappingTargetResourceIsMissing()
    {
        var network = CreateNetworkWithEndpointMapping(providerResourceId: null);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            network,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Endpoint mapping target unavailable", diagnostic.Title);
        Assert.Equal(
            "Mapping 'API' target resource 'api' could not be found.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenEndpointMappingTargetEndpointIsMissing()
    {
        var network = CreateNetworkWithEndpointMapping(providerResourceId: null);
        var api = CreateEndpointResource("application:api", "API", ResourceEndpoint.Http("admin", "api.local", 8081));

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            network,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
            {
                [api.Id] = api
            });

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Endpoint mapping target endpoint unavailable", diagnostic.Title);
        Assert.Equal(
            "Mapping 'API' target endpoint 'http' could not be found on resource 'API'.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenLoadBalancerHostResourceIsMissing()
    {
        var loadBalancer = CreateLoadBalancer("docker:missing");

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(
            loadBalancer,
            new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase));

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Load balancer host unavailable", diagnostic.Title);
        Assert.Equal(
            "Container host resource 'missing' could not be found. Provider-owned load balancer runtime may not be placeable.",
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
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Load balancer route target unavailable", diagnostic.Title);
        Assert.Equal(
            "Route 'API' targets resource 'api', but that resource could not be found.",
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
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Load balancer route endpoint unavailable", diagnostic.Title);
        Assert.Equal(
            "Route 'API' targets endpoint 'http' on resource 'API', but that endpoint could not be found.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenVolumeMountsArePartiallyMaterialized()
    {
        var resource = CreateVolumeConsumer("partial", materializedCount: 1, mountCount: 2);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Storage mounts not fully materialized", diagnostic.Title);
        Assert.Equal(
            "Only some declared storage mounts are materialized. 1 of 2 declared storage mounts are materialized.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenVolumeMountMaterializationIsUnknown()
    {
        var resource = CreateVolumeConsumer("unknown", materializedCount: 0, mountCount: 1);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Storage mounts not fully materialized", diagnostic.Title);
        Assert.Equal(
            "CloudShell has not observed storage mount materialization yet. 0 of 1 declared storage mounts are materialized.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenVolumeMountsAreMaterialized()
    {
        var resource = CreateVolumeConsumer("materialized", materializedCount: 2, mountCount: 2);

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(resource);

        Assert.Empty(diagnostics);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenStorageRuntimeIsUnavailable()
    {
        var resource = CreateStorage(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.StorageRuntimeStatus] = "unavailable",
            [ResourceAttributeNames.StorageRuntimeStatusReason] =
                "Local Storage root '/tmp/cloudshell-missing' does not exist yet."
        });

        var diagnostics = ResourceDiagnosticDisplay.GetDiagnostics(resource);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Storage provider unavailable", diagnostic.Title);
        Assert.Equal(
            "Local Storage root '/tmp/cloudshell-missing' does not exist yet.",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_WarnsWhenStorageOwnedVolumeConsumerReportsUnknownMaterialization()
    {
        var storage = CreateStorage();
        var volume = CreateStorageVolume(storage.Id);
        var consumer = CreateVolumeConsumer(
            "unknown",
            materializedCount: 0,
            mountCount: 1,
            dependsOn: [volume.Id]);

        var diagnostics = StorageResourceDiagnostics.GetDiagnostics(
            storage,
            [volume],
            [storage, volume, consumer]);

        var diagnostic = Assert.Single(diagnostics);
        Assert.Equal(ResourceSignalSeverity.Warning, diagnostic.Severity);
        Assert.Equal("Storage volume mounts not fully materialized", diagnostic.Title);
        Assert.Equal(
            "1 consumer of volumes owned by this Storage resource reports storage mounts that are not fully materialized: API: unknown (0/1 materialized).",
            diagnostic.Message);
    }

    [Fact]
    public void GetDiagnostics_DoesNotWarnWhenStorageOwnedVolumeConsumerReportsMaterialized()
    {
        var storage = CreateStorage();
        var volume = CreateStorageVolume(storage.Id);
        var consumer = CreateVolumeConsumer(
            "materialized",
            materializedCount: 1,
            mountCount: 1,
            dependsOn: [volume.Id]);

        var diagnostics = StorageResourceDiagnostics.GetDiagnostics(
            storage,
            [volume],
            [storage, volume, consumer]);

        Assert.Empty(diagnostics);
    }

    private static Resource CreateNameMapping(
        string providerResourceId,
        string materializationStatus = "ProviderSelected",
        string? materializationStatusReason = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        var resourceAttributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.NameMappingHostName] = "api.local",
            [ResourceAttributeNames.NameMappingTargetResourceId] = "application:api",
            [ResourceAttributeNames.NameMappingExposure] = ResourceExposureScope.Public.ToString(),
            [ResourceAttributeNames.NameMappingStatus] = "Ready",
            [ResourceAttributeNames.NameMappingMaterializationStatus] = materializationStatus,
            [ResourceAttributeNames.NameMappingMaterializationStatusReason] =
                materializationStatusReason ?? "Provider selected.",
            [ResourceAttributeNames.NameMappingProviderResourceId] = providerResourceId
        };
        if (attributes is not null)
        {
            foreach (var (name, value) in attributes)
            {
                resourceAttributes[name] = value;
            }
        }

        return
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
            Attributes: resourceAttributes,
            Capabilities: [new(ResourceCapabilityIds.NetworkingNameMapping)]);
    }

    private static Resource CreateLifecycleResource(
        ResourceState state,
        IReadOnlyList<ResourceHealthCheck>? healthChecks = null,
        string? displayName = null) =>
        new(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            state,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Actions:
            [
                ResourceAction.Start,
                ResourceAction.Restart
            ],
            HealthChecks: healthChecks,
            DisplayName: displayName);

    private static Dictionary<string, Resource> CreateNameMappingRelatedResources(params Resource[] resources)
    {
        var relatedResources = new Dictionary<string, Resource>(StringComparer.OrdinalIgnoreCase)
        {
            ["application:api"] = CreateEndpointResource(
                "application:api",
                "API",
                ResourceEndpoint.Http("http", "api.local", 8080))
        };

        foreach (var resource in resources)
        {
            relatedResources[resource.Id] = resource;
        }

        return relatedResources;
    }

    private static Resource CreateNetworkWithEndpointMapping(string? providerResourceId) =>
        new(
            "network:app",
            "App Network",
            "Network",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [ResourceEndpoint.Http("api", "localhost", 5000)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.NetworkResourceType,
            ResourceClass: ResourceClass.Network,
            Capabilities: [
                new(ResourceCapabilityIds.NetworkingProvider),
                new(ResourceCapabilityIds.NetworkingEndpointProvider),
                new(ResourceCapabilityIds.NetworkingEndpointMapper)
            ],
            EndpointMappings:
            [
                new ResourceEndpointMappingDefinition(
                    "mapping:api",
                    "API",
                    new ResourceEndpointReference("network:app", "api"),
                    new ResourceEndpointReference("application:api", "http"),
                    "network:app",
                    providerResourceId)
            ]);

    private static Resource CreateEndpointResource(
        string id,
        string name,
        ResourceEndpoint endpoint,
        IReadOnlyList<ResourceCapability>? capabilities = null) =>
        new(
            id,
            name,
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [endpoint],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            Capabilities: capabilities);

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

    private static Resource CreateVolumeConsumer(
        string materializationStatus,
        int materializedCount,
        int mountCount,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            "application:api",
            "API",
            "Container app",
            "Applications",
            "local",
            ResourceState.Running,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            ResourceClass: ResourceClass.Container,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.VolumeMountMaterializationStatus] = materializationStatus,
                [ResourceAttributeNames.VolumeMountMaterializedCount] = materializedCount.ToString(CultureInfo.InvariantCulture),
                [ResourceAttributeNames.VolumeMountCount] = mountCount.ToString(CultureInfo.InvariantCulture)
            });

    private static Resource CreateStorage(
        IReadOnlyDictionary<string, string>? additionalAttributes = null)
    {
        var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.StorageProvider] = StorageProviderNames.LocalStorage,
            [ResourceAttributeNames.StorageMedium] = StorageMedia.FileSystem
        };
        foreach (var attribute in additionalAttributes ?? new Dictionary<string, string>())
        {
            attributes[attribute.Key] = attribute.Value;
        }

        return new Resource(
            "storage:local",
            "Local Storage",
            "Local Storage",
            "CloudShell",
            "local",
            ResourceState.Running,
            [],
            "FileSystem",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.StorageResourceType,
            ResourceClass: ResourceClass.Storage,
            Attributes: attributes);
    }

    private static Resource CreateStorageVolume(string storageResourceId) =>
        new(
            "volume:sql-data",
            "SQL Data",
            "Volume",
            "CloudShell",
            "logical",
            ResourceState.Running,
            [],
            "FileSystem",
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.VolumeResourceType,
            ParentResourceId: storageResourceId,
            ResourceClass: ResourceClass.Storage,
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.VolumeStorageResourceId] = storageResourceId,
                [ResourceAttributeNames.VolumeStorageMedium] = StorageMedia.FileSystem
            });
}
