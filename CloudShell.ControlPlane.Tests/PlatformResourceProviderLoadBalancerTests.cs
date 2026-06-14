using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Traefik;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.Tests;

public sealed class PlatformResourceProviderLoadBalancerTests
{
    [Fact]
    public async Task ExecuteActionAsync_AppliesLoadBalancerConfigurationThroughProvider()
    {
        var contentRoot = CreateTempDirectory();
        var outputDirectory = Path.Combine(contentRoot, "traefik");
        var store = new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            new TestHostEnvironment(contentRoot));
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions(),
            loadBalancerProviders:
            [
                new TraefikLoadBalancerProvider(new TraefikProviderOptions
                {
                    DynamicConfigurationDirectory = outputDirectory
                })
            ]);
        var definition = new LoadBalancerResourceDefinition(
            "load-balancer:public",
            "Public",
            "traefik",
            HostResourceId: "docker:engine",
            Entrypoints:
            [
                new LoadBalancerEntrypoint("web", ResourceEndpointProtocol.Http, 80),
                new LoadBalancerEntrypoint("postgres", ResourceEndpointProtocol.Tcp, 5432)
            ],
            Routes:
            [
                new LoadBalancerRoute(
                    "web-app",
                    "Web app",
                    LoadBalancerRouteKind.Http,
                    "web",
                    new LoadBalancerRouteMatch("app.local", "/"),
                    new LoadBalancerRouteTarget("application:web", "http")),
                new LoadBalancerRoute(
                    "api",
                    "API",
                    LoadBalancerRouteKind.Http,
                    "web",
                    new LoadBalancerRouteMatch("api.local", "/v1"),
                    new LoadBalancerRouteTarget("application:api", Port: 5000)),
                new LoadBalancerRoute(
                    "postgres",
                    "Postgres",
                    LoadBalancerRouteKind.Tcp,
                    "postgres",
                    new LoadBalancerRouteMatch(Port: 5432),
                    new LoadBalancerRouteTarget("application:postgres", Port: 5432))
            ]);
        await provider.SetupLoadBalancerAsync(
            definition,
            null,
            new TestResourceRegistrationStore([]));
        var loadBalancer = provider.GetResources().Single(resource => resource.Id == "load-balancer:public");
        var resourceManager = new TestResourceManagerStore(
            [
                loadBalancer,
                CreateResource(
                    "docker:engine",
                    "Local Docker",
                    [ResourceEndpoint.FromAddress("engine", "unix:///var/run/docker.sock", "docker")]),
                CreateResource(
                    "application:web",
                    "Web",
                    [ResourceEndpoint.Http("http", "web.internal", 8080)]),
                CreateResource(
                    "application:api",
                    "API",
                    [],
                    new Dictionary<string, string>
                    {
                        [ResourceAttributeNames.ContainerReplicas] = "3"
                    }),
                CreateResource(
                    "application:postgres",
                    "Postgres",
                    [ResourceEndpoint.Tcp("postgres", "postgres.internal", 5432)])
            ]);
        var context = new ResourceProcedureContext(
            loadBalancer,
            null,
            null,
            new TestResourceRegistrationStore([]),
            resourceManager);

        var result = await provider.ExecuteActionAsync(
            context,
            loadBalancer.ResourceActions.Single(action =>
                action.Id == PlatformResourceProvider.ApplyLoadBalancerConfigurationActionId));

        var config = await File.ReadAllTextAsync(Path.Combine(outputDirectory, "load-balancer-public.dynamic.yml"));
        Assert.Contains("Applied Traefik configuration for 3 route(s)", result.Message);
        Assert.Contains("Host(`app.local`) && PathPrefix(`/`)", config);
        Assert.Contains("url: \"http://web.internal:8080\"", config);
        Assert.Contains("Host(`api.local`) && PathPrefix(`/v1`)", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-1:5000\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-2:5000\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-3:5000\"", config);
        Assert.Contains("rule: \"HostSNI(`*`)\"", config);
        Assert.Contains("address: \"postgres.internal:5432\"", config);
    }

    [Fact]
    public async Task ExecuteActionAsync_StartsAndStopsLoadBalancerRuntimeThroughProvider()
    {
        var store = CreatePlatformStore();
        var runtimeProvider = new TestLoadBalancerRuntimeProvider();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions(),
            loadBalancerProviders: [runtimeProvider]);
        var definition = CreateRuntimeLoadBalancerDefinition();
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupLoadBalancerAsync(definition, null, registrations);

        var loadBalancer = provider.GetResources().Single(resource => resource.Id == definition.Id);
        Assert.Equal(ResourceState.Stopped, loadBalancer.State);
        Assert.Contains(loadBalancer.ResourceActions, action => action.Id == ResourceActionIds.Start);

        var resourceManager = CreateRuntimeResourceManager(loadBalancer);
        var startResult = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(loadBalancer, null, null, registrations, resourceManager),
            loadBalancer.StartAction!);

        Assert.Equal("Started test load balancer runtime.", startResult.Message);
        var started = Assert.Single(runtimeProvider.Started);
        Assert.Single(started.Routes);
        Assert.Equal(ResourceState.Running, store.GetLoadBalancer(definition.Id)!.RuntimeState);

        loadBalancer = provider.GetResources().Single(resource => resource.Id == definition.Id);
        Assert.Equal(ResourceState.Running, loadBalancer.State);
        Assert.Contains(loadBalancer.ResourceActions, action => action.Id == ResourceActionIds.Stop);

        var stopResult = await provider.ExecuteActionAsync(
            new ResourceProcedureContext(loadBalancer, null, null, registrations, resourceManager),
            loadBalancer.StopAction!);

        Assert.Equal("Stopped test load balancer runtime.", stopResult.Message);
        Assert.Single(runtimeProvider.Stopped);
        Assert.Equal(ResourceState.Stopped, store.GetLoadBalancer(definition.Id)!.RuntimeState);
    }

    [Fact]
    public async Task SetupLoadBalancerAsync_RejectsRouteWithMissingEntrypoint()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions());
        var definition = CreateRuntimeLoadBalancerDefinition() with
        {
            Entrypoints = [],
            Routes =
            [
                CreateRuntimeLoadBalancerDefinition().LoadBalancerRoutes.Single()
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupLoadBalancerAsync(
                definition,
                null,
                new TestResourceRegistrationStore([])));

        Assert.Contains("references entrypoint 'web'", exception.Message);
        Assert.Null(store.GetLoadBalancer(definition.Id));
    }

    [Fact]
    public async Task SetupLoadBalancerAsync_RejectsDuplicateEntrypointNames()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions());
        var definition = CreateRuntimeLoadBalancerDefinition() with
        {
            Entrypoints =
            [
                new LoadBalancerEntrypoint("web", ResourceEndpointProtocol.Http, 8080),
                new LoadBalancerEntrypoint(" WEB ", ResourceEndpointProtocol.Http, 8081)
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupLoadBalancerAsync(
                definition,
                null,
                new TestResourceRegistrationStore([])));

        Assert.Contains("multiple entrypoints named 'web'", exception.Message);
        Assert.Null(store.GetLoadBalancer(definition.Id));
    }

    [Fact]
    public async Task SetupLoadBalancerAsync_RejectsDuplicateRouteIds()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions());
        var route = CreateRuntimeLoadBalancerDefinition().LoadBalancerRoutes.Single();
        var definition = CreateRuntimeLoadBalancerDefinition() with
        {
            Routes =
            [
                route,
                route with
                {
                    Id = " WEB-APP ",
                    Name = "Other route",
                    Match = new LoadBalancerRouteMatch("other.local", "/"),
                    Target = new LoadBalancerRouteTarget("application:other", "http")
                }
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupLoadBalancerAsync(
                definition,
                null,
                new TestResourceRegistrationStore([])));

        Assert.Contains("multiple routes with id 'web-app'", exception.Message);
        Assert.Null(store.GetLoadBalancer(definition.Id));
    }

    [Fact]
    public async Task SetupLoadBalancerAsync_RejectsDuplicateRouteMatches()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions());
        var route = CreateRuntimeLoadBalancerDefinition().LoadBalancerRoutes.Single();
        var definition = CreateRuntimeLoadBalancerDefinition() with
        {
            Routes =
            [
                route,
                route with
                {
                    Id = "web-app-copy",
                    Name = "Web app copy",
                    Target = new LoadBalancerRouteTarget("application:web-copy", "http")
                }
            ]
        };

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.SetupLoadBalancerAsync(
                definition,
                null,
                new TestResourceRegistrationStore([])));

        Assert.Contains("conflicting route match", exception.Message);
        Assert.Contains("web-app", exception.Message);
        Assert.Contains("web-app-copy", exception.Message);
        Assert.Null(store.GetLoadBalancer(definition.Id));
    }

    [Fact]
    public async Task GetActionUnavailableReasonAsync_ReturnsMissingProviderReasonForApply()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(store, new PlatformResourceOptions());
        var definition = CreateRuntimeLoadBalancerDefinition() with
        {
            Provider = "missing-provider"
        };
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupLoadBalancerAsync(definition, null, registrations);
        var loadBalancer = provider.GetResources().Single(resource => resource.Id == definition.Id);
        var resourceManager = CreateRuntimeResourceManager(loadBalancer);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(loadBalancer, null, null, registrations, resourceManager),
            loadBalancer.ResourceActions.Single(action => action.Id == PlatformResourceProvider.ApplyLoadBalancerConfigurationActionId));

        Assert.Equal(
            "No activated load balancer provider can apply provider 'missing-provider' for resource 'load-balancer:runtime'.",
            reason);
    }

    [Fact]
    public async Task GetActionUnavailableReasonAsync_ReturnsRouteResolutionReasonForApply()
    {
        var store = CreatePlatformStore();
        var runtimeProvider = new TestLoadBalancerRuntimeProvider();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions(),
            loadBalancerProviders: [runtimeProvider]);
        var definition = CreateRuntimeLoadBalancerDefinition();
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupLoadBalancerAsync(definition, null, registrations);
        var loadBalancer = provider.GetResources().Single(resource => resource.Id == definition.Id);
        var resourceManager = new TestResourceManagerStore([loadBalancer]);

        var reason = await ((IResourceActionAvailabilityProvider)provider).GetActionUnavailableReasonAsync(
            new ResourceProcedureContext(loadBalancer, null, null, registrations, resourceManager),
            loadBalancer.ResourceActions.Single(action => action.Id == PlatformResourceProvider.ApplyLoadBalancerConfigurationActionId));

        Assert.Equal(
            "Load balancer resource 'load-balancer:runtime' host resource 'docker:engine' could not be found.",
            reason);
    }

    [Fact]
    public async Task DeleteAsync_CleansLoadBalancerRuntimeBeforeRemovingRegistration()
    {
        var store = CreatePlatformStore();
        var runtimeProvider = new TestLoadBalancerRuntimeProvider();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions(),
            loadBalancerProviders: [runtimeProvider]);
        var definition = CreateRuntimeLoadBalancerDefinition();
        var registrations = new TestResourceRegistrationStore([]);
        await provider.SetupLoadBalancerAsync(definition, null, registrations);
        var loadBalancer = provider.GetResources().Single(resource => resource.Id == definition.Id);
        var resourceManager = CreateRuntimeResourceManager(loadBalancer);

        var result = await provider.DeleteAsync(
            new ResourceProcedureContext(loadBalancer, null, null, registrations, resourceManager));

        Assert.Contains("Deleted test load balancer runtime.", result.Message);
        Assert.Null(store.GetLoadBalancer(definition.Id));
        Assert.Null(registrations.GetRegistration(definition.Id));
        Assert.Single(runtimeProvider.Deleted);
    }

    [Fact]
    public async Task DeleteAsync_RejectsVolumeUsedByResource()
    {
        var store = CreatePlatformStore();
        var provider = new PlatformResourceProvider(
            store,
            new PlatformResourceOptions());
        var registrations = new TestResourceRegistrationStore([]);
        var definition = new VolumeResourceDefinition("volume:data", "Data");
        await provider.SetupVolumeAsync(definition, null, registrations);
        var volume = provider.GetResources().Single(resource => resource.Id == definition.Id);
        var app = CreateResource(
            "application:api",
            "API",
            [],
            dependsOn: [volume.Id]);
        var resourceManager = new TestResourceManagerStore([volume, app]);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.DeleteAsync(
                new ResourceProcedureContext(volume, null, null, registrations, resourceManager)));

        Assert.Contains("cannot be deleted because it is used by: API", exception.Message, StringComparison.Ordinal);
        Assert.NotNull(store.GetVolume(definition.Id));
        Assert.NotNull(registrations.GetRegistration(definition.Id));
    }

    private static Resource CreateResource(
        string id,
        string name,
        IReadOnlyList<ResourceEndpoint> endpoints,
        IReadOnlyDictionary<string, string>? attributes = null,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            id,
            name,
            "Test",
            "Test",
            "test",
            ResourceState.Running,
            endpoints,
            "test",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            Attributes: attributes);

    private static PlatformResourceStore CreatePlatformStore()
    {
        var contentRoot = CreateTempDirectory();
        return new PlatformResourceStore(
            new PlatformResourceOptions
            {
                DefinitionsPath = "platform-resources.json"
            },
            new TestHostEnvironment(contentRoot));
    }

    private static LoadBalancerResourceDefinition CreateRuntimeLoadBalancerDefinition() =>
        new(
            "load-balancer:runtime",
            "Runtime",
            "test-runtime",
            HostResourceId: "docker:engine",
            Entrypoints: [new LoadBalancerEntrypoint("web", ResourceEndpointProtocol.Http, 8080)],
            Routes:
            [
                new LoadBalancerRoute(
                    "web-app",
                    "Web app",
                    LoadBalancerRouteKind.Http,
                    "web",
                    new LoadBalancerRouteMatch("app.local", "/"),
                    new LoadBalancerRouteTarget("application:web", "http"))
            ]);

    private static IResourceManagerStore CreateRuntimeResourceManager(Resource loadBalancer) =>
        new TestResourceManagerStore(
            [
                loadBalancer,
                CreateResource(
                    "docker:engine",
                    "Local Docker",
                    [ResourceEndpoint.FromAddress("engine", "unix:///var/run/docker.sock", "docker")]),
                CreateResource(
                    "application:web",
                    "Web",
                    [ResourceEndpoint.Http("http", "web.internal", 8080)])
            ]);

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"cloudshell-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
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

    private sealed class TestResourceRegistrationStore(IReadOnlyList<ResourceRegistration> registrations) :
        IResourceRegistrationStore
    {
        private readonly Dictionary<string, ResourceRegistration> _registrations = registrations.ToDictionary(
            registration => registration.ResourceId,
            StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<ResourceRegistration> GetRegistrations() => _registrations.Values.ToArray();

        public ResourceRegistration? GetRegistration(string resourceId) =>
            _registrations.GetValueOrDefault(resourceId);

        public Task RegisterAsync(
            string providerId,
            string resourceId,
            string? resourceGroupId = null,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default)
        {
            _registrations[resourceId] = new ResourceRegistration(
                resourceId,
                providerId,
                resourceGroupId,
                DateTimeOffset.UtcNow,
                dependsOn ?? []);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(
            string resourceId,
            CancellationToken cancellationToken = default)
        {
            _registrations.Remove(resourceId);
            return Task.CompletedTask;
        }

        public Task AssignToGroupAsync(
            string resourceId,
            string? resourceGroupId,
            IReadOnlyList<string>? dependsOn = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new PhysicalFileProvider(contentRootPath);
    }

    private sealed class TestLoadBalancerRuntimeProvider : ILoadBalancerRuntimeProvider
    {
        public List<LoadBalancerProviderContext> Started { get; } = [];

        public List<LoadBalancerProviderContext> Stopped { get; } = [];

        public List<LoadBalancerProviderContext> Deleted { get; } = [];

        public string ProviderName => "test-runtime";

        public bool CanApply(LoadBalancerProviderContext context) => true;

        public bool CanManageRuntime(LoadBalancerResourceDefinition definition) => true;

        public Task<ResourceProcedureResult> ApplyAsync(
            LoadBalancerProviderContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed("Applied test load balancer configuration."));

        public Task<ResourceProcedureResult> StartAsync(
            LoadBalancerProviderContext context,
            CancellationToken cancellationToken = default)
        {
            Started.Add(context);
            return Task.FromResult(ResourceProcedureResult.Completed("Started test load balancer runtime."));
        }

        public Task<ResourceProcedureResult> StopAsync(
            LoadBalancerProviderContext context,
            CancellationToken cancellationToken = default)
        {
            Stopped.Add(context);
            return Task.FromResult(ResourceProcedureResult.Completed("Stopped test load balancer runtime."));
        }

        public Task<ResourceProcedureResult> DeleteAsync(
            LoadBalancerProviderContext context,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(context);
            return Task.FromResult(ResourceProcedureResult.Completed("Deleted test load balancer runtime."));
        }
    }
}
