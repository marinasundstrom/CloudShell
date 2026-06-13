using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Logs;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Tests;

public sealed class HostScopedResourceShutdownServiceTests
{
    [Fact]
    public async Task StopAsync_StopsHostScopedResourcesInDependentFirstOrder()
    {
        var vault = CreateResource("vault", "Vault", ResourceState.Running);
        var api = CreateResource("api", "API", ResourceState.Running, ["vault"]);
        var detached = CreateResource("detached", "Detached", ResourceState.Running);
        var stopped = CreateResource("stopped", "Stopped", ResourceState.Stopped);
        var noStopAction = CreateResource("no-stop", "No Stop", ResourceState.Running, actions: []);
        var catalog = new TestResourceOrchestrationCatalog(new ResourceOrchestrationCatalogSnapshot(
            [vault, api, detached, stopped, noStopAction],
            new Dictionary<string, ResourceWorkloadConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                [vault.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped),
                [api.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped),
                [detached.Id] = CreateWorkload(ResourceLifetime.Detached),
                [stopped.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped),
                [noStopAction.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped)
            },
            new Dictionary<string, ContainerHostDescriptor>(StringComparer.OrdinalIgnoreCase)));
        var orchestrator = new RecordingResourceOrchestrator();
        var resourceEvents = new InMemoryResourceEventStore();
        using var services = CreateServices(catalog, orchestrator, resourceEvents);

        await services.GetRequiredService<HostScopedResourceShutdownService>()
            .StopAsync(CancellationToken.None);

        Assert.Equal(["api", "vault"], orchestrator.ExecutedActions.Select(action => action.ResourceId));
        Assert.All(orchestrator.ExecutedActions, action =>
        {
            Assert.Equal(ResourceActionIds.Stop, action.ActionId);
        });
        Assert.Contains(
            resourceEvents.GetEvents(new ResourceEventQuery(ResourceId: "api")),
            resourceEvent =>
                resourceEvent.EventType == ResourceEventTypes.Actions.Lifecycle.Stop &&
                resourceEvent.TriggeredBy == HostScopedResourceShutdownService.ShutdownTrigger);
    }

    [Fact]
    public async Task StopAsync_ContinuesWhenAHostScopedResourceStopFails()
    {
        var api = CreateResource("api", "API", ResourceState.Running);
        var worker = CreateResource("worker", "Worker", ResourceState.Running);
        var catalog = new TestResourceOrchestrationCatalog(new ResourceOrchestrationCatalogSnapshot(
            [api, worker],
            new Dictionary<string, ResourceWorkloadConfiguration>(StringComparer.OrdinalIgnoreCase)
            {
                [api.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped),
                [worker.Id] = CreateWorkload(ResourceLifetime.ControlPlaneScoped)
            },
            new Dictionary<string, ContainerHostDescriptor>(StringComparer.OrdinalIgnoreCase)));
        var orchestrator = new RecordingResourceOrchestrator
        {
            FailingResourceIds = { "api" }
        };
        using var services = CreateServices(catalog, orchestrator, new InMemoryResourceEventStore());

        await services.GetRequiredService<HostScopedResourceShutdownService>()
            .StopAsync(CancellationToken.None);

        Assert.Equal(["api", "worker"], orchestrator.ExecutedActions.Select(action => action.ResourceId));
    }

    private static ServiceProvider CreateServices(
        TestResourceOrchestrationCatalog catalog,
        RecordingResourceOrchestrator orchestrator,
        InMemoryResourceEventStore resourceEvents)
    {
        var resourceStore = new TestResourceManagerStore(catalog.Snapshot.Resources);
        var registrations = new TestResourceRegistrationStore();
        var environment = new TestHostEnvironment();
        var selectionStore = new ResourceOrchestratorSelectionStore(
            environment,
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IHostEnvironment>(environment)
            .AddSingleton(resourceStore)
            .AddSingleton<IResourceManagerStore>(resourceStore)
            .AddSingleton<IResourceRegistrationStore>(registrations)
            .AddSingleton<IResourceOrchestrationCatalog>(catalog)
            .AddSingleton(resourceEvents)
            .AddSingleton<IResourceEventSink>(resourceEvents)
            .AddSingleton(orchestrator)
            .AddSingleton<ResourceOrchestrationService>(serviceProvider =>
                new ResourceOrchestrationService(
                    [serviceProvider.GetRequiredService<RecordingResourceOrchestrator>()],
                    [],
                    serviceProvider.GetRequiredService<IResourceManagerStore>(),
                    serviceProvider.GetRequiredService<IResourceRegistrationStore>(),
                    new ResourceDeclarationStore(),
                    selectionStore,
                    resourceEvents: serviceProvider.GetRequiredService<IResourceEventSink>()))
            .AddSingleton<HostScopedResourceShutdownService>()
            .BuildServiceProvider();
    }

    private static Resource CreateResource(
        string id,
        string name,
        ResourceState state,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<ResourceAction>? actions = null) =>
        new(
            id,
            name,
            "test.resource",
            "Test",
            "local",
            state,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            Actions: actions ?? [ResourceAction.Stop]);

    private static ResourceWorkloadConfiguration CreateWorkload(ResourceLifetime lifetime) =>
        new(ResourceWorkloadKind.LocalExecutable, "test", Lifetime: lifetime);

    private sealed class TestResourceOrchestrationCatalog(
        ResourceOrchestrationCatalogSnapshot snapshot) : IResourceOrchestrationCatalog
    {
        public ResourceOrchestrationCatalogSnapshot Snapshot => snapshot;

        public Task<ResourceOrchestrationCatalogSnapshot> GetSnapshotAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(snapshot);
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class RecordingResourceOrchestrator : IResourceOrchestrator
    {
        public string Id => "default";

        public string DisplayName => "Test";

        public List<(string ResourceId, string ActionId)> ExecutedActions { get; } = [];

        public HashSet<string> FailingResourceIds { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool CanExecute(
            ResourceOrchestrationContext context,
            ResourceAction action) =>
            action.Kind == ResourceActionKind.Stop;

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceOrchestrationContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add((context.Resource.Id, action.Id));
            if (FailingResourceIds.Contains(context.Resource.Id))
            {
                throw new InvalidOperationException($"Could not stop {context.Resource.Id}.");
            }

            return Task.FromResult(ResourceProcedureResult.Completed($"Stopped {context.Resource.Id}."));
        }

        public bool CanDelete(ResourceOrchestrationContext context) => false;

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceOrchestrationContext context,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
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

        public bool IsRegistered(string resourceId) =>
            resources.Any(resource => string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestResourceRegistrationStore : IResourceRegistrationStore
    {
        public IReadOnlyList<ResourceRegistration> GetRegistrations() => [];

        public ResourceRegistration? GetRegistration(string resourceId) => null;

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
            Task.CompletedTask;

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

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
