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

    private static Resource CreateResource(
        string id,
        string name,
        IReadOnlyList<ResourceEndpoint> endpoints,
        IReadOnlyDictionary<string, string>? attributes = null) =>
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
            [],
            Attributes: attributes);

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
}
