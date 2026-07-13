using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;
using CloudShell.ControlPlane.ResourceManager.Platform;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class LocalHostNamePublishingProviderTests
{
    [Fact]
    public async Task PlatformProvider_ReconcileNameMappingsActionPublishesLocalHostNames()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var options = new PlatformResourceOptions
        {
            DefinitionsPath = "platform-resources.json",
            LocalHostNameHostsFilePath = hostsPath
        };
        var store = new PlatformResourceStore(options, new TestHostEnvironment(contentRoot));
        var localPublisher = new LocalHostNamePublishingProvider(options);
        var platform = new PlatformResourceProvider(
            store,
            options,
            namePublishingProviders: [localPublisher]);
        await platform.SetupDnsZoneAsync(
            new DnsZoneResourceDefinition(
                "dns:dev",
                "Development DNS",
                "cloudshell.local",
                Provider: LocalHostNamePublishingProvider.DefaultProviderName,
                Mappings:
                [
                    new DnsNameMappingDefinition(
                        "dns:dev:name:api",
                        "api.cloudshell.local",
                        "api.cloudshell.local",
                        "application:api",
                        "http")
                ]),
            null,
            new TestResourceRegistrationStore([]));
        var zone = platform.GetResources().Single(resource => resource.Id == "dns:dev");
        var api = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080)],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings:
            [
                new ResourceEndpointNetworkMapping(
                    "application:api:endpoint-network-mapping:http",
                    "http",
                    new ResourceEndpointReference("application:api", "http"),
                    "http://localhost:5080",
                    ResourceExposureScope.Local,
                    SourceEndpointName: "http")
            ]);
        var resourceEvents = new InMemoryResourceEventStore();

        var result = await platform.ExecuteActionAsync(
            new ResourceProcedureContext(
                zone,
                null,
                null,
                new TestResourceRegistrationStore([]),
                new TestResourceManagerStore([zone, api]),
                TriggeredBy: "operator",
                ResourceEvents: resourceEvents),
            zone.ResourceActions.Single(action =>
                action.Id == PlatformResourceProvider.ReconcileNameMappingsActionId));

        var content = await File.ReadAllTextAsync(hostsPath);
        Assert.Contains("Published 1 local host name mapping", result.Message);
        Assert.Contains("127.0.0.1 api.cloudshell.local", content);
        var projectedMapping = platform.GetResources().Single(resource => resource.Id == "dns:dev:name:api");
        Assert.Equal(
            hostsPath,
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingLocalHostNamesHostsFilePath]);
        Assert.Equal(
            "Custom",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingLocalHostNamesHostsFileTarget]);
        Assert.Equal(
            "Skipped",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshStatus]);
        Assert.Contains(
            "custom hosts-file target",
            projectedMapping.ResourceAttributes[ResourceAttributeNames.NameMappingLocalHostNamesResolverRefreshReason],
            StringComparison.Ordinal);
        var events = resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: zone.Id));
        Assert.Contains(events, resourceEvent =>
            resourceEvent.EventType == ResourceEventTypes.Events.Provider.ForEvent(
                PlatformResourceProvider.ProviderId,
                "dns.nameMappings.published") &&
            resourceEvent.TriggeredBy == "operator" &&
            resourceEvent.Message.Contains("applied DNS name mappings", StringComparison.OrdinalIgnoreCase) &&
            resourceEvent.Message.Contains("Published 1 local host name mapping", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReconcileAsync_WritesExactHostMappingsToConfiguredHostsFile()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        await File.WriteAllTextAsync(hostsPath, "127.0.0.1 localhost" + Environment.NewLine);
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        var result = await provider.ReconcileAsync(CreateContext(
            "cloudshell.local",
            "api.cloudshell.local",
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
            "http://localhost:5080"));

        var content = await File.ReadAllTextAsync(hostsPath);
        Assert.Contains("Published 1 local host name mapping", result.Message);
        Assert.Contains("127.0.0.1 localhost", content);
        Assert.Contains("# BEGIN CloudShell local hostnames", content);
        Assert.Contains("127.0.0.1 api.cloudshell.local", content);
        Assert.Contains("# END CloudShell local hostnames", content);
    }

    [Fact]
    public async Task ReconcileAsync_UsesEndpointNetworkMappingAddress()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        var result = await provider.ReconcileAsync(CreateContext(
            "cloudshell.local",
            "api.cloudshell.local",
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
            "http://localhost:5080"));

        var content = await File.ReadAllTextAsync(hostsPath);
        Assert.Contains("Published 1 local host name mapping", result.Message);
        Assert.Contains("127.0.0.1 api.cloudshell.local", content);
    }

    [Fact]
    public async Task ReconcileAsync_ReplacesPreviousCloudShellBlock()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        await File.WriteAllTextAsync(
            hostsPath,
            string.Join(
                Environment.NewLine,
                [
                    "127.0.0.1 localhost",
                    "# BEGIN CloudShell local hostnames",
                    "127.0.0.1 old.cloudshell.local",
                    "# END CloudShell local hostnames",
                    string.Empty
                ]));
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        await provider.ReconcileAsync(CreateContext(
            "cloudshell.local",
            "api.cloudshell.local",
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
            "http://127.0.0.1:5080"));

        var content = await File.ReadAllTextAsync(hostsPath);
        Assert.DoesNotContain("old.cloudshell.local", content);
        Assert.Contains("127.0.0.1 localhost", content);
        Assert.Contains("127.0.0.1 api.cloudshell.local", content);
    }

    [Fact]
    public async Task ReconcileAsync_WarnsForLocalSuffix()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        var result = await provider.ReconcileAsync(CreateContext(
            "local",
            "api.local",
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
            "http://localhost:5080"));

        Assert.Contains(".local host names may conflict", result.Message);
    }

    [Fact]
    public async Task ReconcileAsync_SkipsResolverRefreshForConfiguredHostsFile()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var refresher = new TestResolverCacheRefresher();
        var provider = new LocalHostNamePublishingProvider(
            new PlatformResourceOptions
            {
                LocalHostNameHostsFilePath = hostsPath
            },
            refresher);

        var result = await provider.ReconcileAsync(CreateContext(
            "cloudshell.local",
            "api.cloudshell.local",
            ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
            "http://localhost:5080"));

        Assert.False(refresher.Called);
        Assert.Contains("custom hosts-file target", result.Message);
    }

    [Fact]
    public async Task ResolverCacheRefresher_RunsPlatformCommandsUntilOneSucceeds()
    {
        var runner = new TestResolverCacheRefreshCommandRunner(failCount: 1);
        var refresher = new LocalHostNameResolverCacheRefresher(runner);

        var result = await refresher.RefreshAsync(LocalHostNameResolverRefreshPlatform.MacOS);

        Assert.True(result.Attempted);
        Assert.Equal(2, runner.Commands.Count);
        Assert.True(result.Succeeded);
    }

    [Fact]
    public void ResolverCacheRefresher_DefinesPlatformRefreshCommands()
    {
        Assert.Contains(
            LocalHostNameResolverCacheRefresher.GetPlatformCommands(LocalHostNameResolverRefreshPlatform.MacOS),
            command => command.FileName == "dscacheutil" && command.Arguments.SequenceEqual(["-flushcache"]));
        Assert.Contains(
            LocalHostNameResolverCacheRefresher.GetPlatformCommands(LocalHostNameResolverRefreshPlatform.Windows),
            command => command.FileName == "ipconfig" && command.Arguments.SequenceEqual(["/flushdns"]));
        Assert.Contains(
            LocalHostNameResolverCacheRefresher.GetPlatformCommands(LocalHostNameResolverRefreshPlatform.Linux),
            command => command.FileName == "resolvectl" && command.Arguments.SequenceEqual(["flush-caches"]));
    }

    [Fact]
    public void ResolverCacheRefreshPlanner_SelectsOnlyAvailableLinuxTools()
    {
        var planner = new LocalHostNameResolverCacheRefreshCommandPlanner(
            new HostOperatingSystem(HostOperatingSystemKind.Linux, "ubuntu"),
            new TestHostToolResolver("nscd"));

        var plan = planner.CreatePlan();

        var command = Assert.Single(plan.Commands);
        Assert.Equal("nscd", command.FileName);
        Assert.Equal(new[] { "-i", "hosts" }, command.Arguments);
        Assert.Equal(string.Empty, plan.UnavailableReason);
    }

    [Fact]
    public async Task ResolverCacheRefresher_DoesNotRunWhenNoPlatformToolAvailable()
    {
        var runner = new TestResolverCacheRefreshCommandRunner(failCount: 0);
        var planner = new LocalHostNameResolverCacheRefreshCommandPlanner(
            new HostOperatingSystem(HostOperatingSystemKind.Linux, "ubuntu"),
            new TestHostToolResolver());
        var refresher = new LocalHostNameResolverCacheRefresher(runner, planner);

        var result = await refresher.RefreshAsync();

        Assert.False(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Empty(runner.Commands);
        Assert.Contains("no supported Linux resolver cache tool", result.Message);
        Assert.Contains("resolvectl", result.Message);
    }

    [Fact]
    public void ResolverCacheRefreshPlanner_ReportsUnsupportedPlatform()
    {
        var planner = new LocalHostNameResolverCacheRefreshCommandPlanner(
            new HostOperatingSystem(HostOperatingSystemKind.Unknown),
            new TestHostToolResolver("ipconfig"));

        var plan = planner.CreatePlan();

        Assert.Empty(plan.Commands);
        Assert.Contains("operating system has no configured refresh command", plan.UnavailableReason);
    }

    [Fact]
    public void HostOperatingSystem_NormalizesLinuxDistributionId()
    {
        var operatingSystem = new HostOperatingSystem(HostOperatingSystemKind.Linux, " Ubuntu ");

        Assert.True(operatingSystem.IsLinux);
        Assert.Equal("ubuntu", operatingSystem.LinuxDistributionId);
        Assert.Equal("linux/ubuntu", operatingSystem.DisplayName);
    }

    [Fact]
    public async Task ReconcileAsync_RejectsWildcardHostMappings()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ReconcileAsync(CreateContext(
                "cloudshell.local",
                "*.cloudshell.local",
                ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
                "http://localhost:5080")));

        Assert.Contains("only supports exact host mappings", exception.Message);
    }

    [Fact]
    public async Task ReconcileAsync_RejectsNonLocalEndpointHosts()
    {
        var contentRoot = CreateTempDirectory();
        var hostsPath = Path.Combine(contentRoot, "hosts");
        var provider = new LocalHostNamePublishingProvider(new PlatformResourceOptions
        {
            LocalHostNameHostsFilePath = hostsPath
        });

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.ReconcileAsync(CreateContext(
                "cloudshell.local",
                "api.cloudshell.local",
                ResourceEndpoint.Contract("http", "http", ResourceExposureScope.Local, 5080),
                "http://api.internal:5080")));

        Assert.Contains("is not a local or IP address", exception.Message);
    }

    private static DnsNamePublishingContext CreateContext(
        string zoneName,
        string hostName,
        ResourceEndpoint endpoint,
        string? endpointNetworkMappingAddress = null)
    {
        var zone = new DnsZoneResourceDefinition(
            "dns:dev",
            "Development DNS",
            zoneName,
            Provider: LocalHostNamePublishingProvider.DefaultProviderName,
            Mappings:
            [
                new DnsNameMappingDefinition(
                    "dns:dev:name:api",
                    hostName,
                    hostName,
                    "application:api",
                    endpoint.Name)
            ]);
        var zoneResource = new Resource(
            zone.Id,
            zone.Name,
            "DNS Zone",
            "CloudShell",
            "logical",
            null,
            [],
            zone.ZoneName,
            DateTimeOffset.UtcNow,
            [],
            TypeId: PlatformResourceProvider.DnsZoneResourceType,
            ResourceClass: ResourceClass.Network);
        var target = new Resource(
            "application:api",
            "API",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            [endpoint],
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings: string.IsNullOrWhiteSpace(endpointNetworkMappingAddress)
                ? null
                :
                [
                    new ResourceEndpointNetworkMapping(
                        "application:api:endpoint-network-mapping:http",
                        endpoint.Name,
                        new ResourceEndpointReference("application:api", endpoint.Name),
                        endpointNetworkMappingAddress,
                        endpoint.Exposure,
                        SourceEndpointName: endpoint.Name)
                ]);
        var mapping = new DnsNameMappingResolution(
            zone.DnsNameMappings.Single(),
            target,
            endpoint,
            target.ResourceEndpointNetworkMappings.FirstOrDefault(endpointMapping =>
                string.Equals(endpointMapping.Target.EndpointName, endpoint.Name, StringComparison.OrdinalIgnoreCase)));

        return new DnsNamePublishingContext(
            zoneResource,
            zone,
            [mapping],
            [],
            new TestResourceManagerStore([zoneResource, target]));
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private sealed class TestResolverCacheRefresher : ILocalHostNameResolverCacheRefresher
    {
        public bool Called { get; private set; }

        public Task<LocalHostNameResolverRefreshResult> RefreshAsync(
            CancellationToken cancellationToken = default)
        {
            Called = true;
            return Task.FromResult(LocalHostNameResolverRefreshResult.Success("Refreshed resolver cache."));
        }
    }

    private sealed class TestResolverCacheRefreshCommandRunner(int failCount) :
        ILocalHostNameResolverCacheRefreshCommandRunner
    {
        public List<LocalHostNameResolverCacheRefreshCommand> Commands { get; } = [];

        public Task<LocalHostNameResolverCacheRefreshCommandResult> RunAsync(
            LocalHostNameResolverCacheRefreshCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            return Task.FromResult(Commands.Count <= failCount
                ? LocalHostNameResolverCacheRefreshCommandResult.Failed($"`{command.DisplayName}` failed.")
                : LocalHostNameResolverCacheRefreshCommandResult.Success($"`{command.DisplayName}` succeeded."));
        }
    }

    private sealed class TestHostToolResolver(params string[] availableToolNames) : IHostToolResolver
    {
        private readonly HashSet<string> availableTools = new(
            availableToolNames,
            StringComparer.OrdinalIgnoreCase);

        public bool IsAvailable(string fileName) => availableTools.Contains(fileName);
    }

    private sealed class TestResourceManagerStore(IReadOnlyList<Resource> resources) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => [];

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public IReadOnlyList<ResourceModelDiagnostic> GetResourceModelDiagnostics() => [];

        public ResourceClass? GetResourceTypeClass(string resourceType) => null;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) => GetResource(resourceId) is not null;
    }

    private sealed class TestResourceRegistrationStore(
        IReadOnlyList<ResourceRegistration> registrations) : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => registrations;

        public ResourceRegistration? GetRegistration(string resourceId) =>
            registrations.FirstOrDefault(registration =>
                string.Equals(registration.ResourceId, resourceId, StringComparison.OrdinalIgnoreCase));

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }
}
