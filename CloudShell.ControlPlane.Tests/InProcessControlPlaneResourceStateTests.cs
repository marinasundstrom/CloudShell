using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.Tests;

public sealed class InProcessControlPlaneResourceStateTests
{
    public static TheoryData<ResourceState, string[]> StateResourceActionCapabilities =>
        new()
        {
            { ResourceState.Running, ["stop", "pause", "restart"] },
            { ResourceState.Starting, ["stop", "restart"] },
            { ResourceState.Paused, ["run", "stop"] },
            { ResourceState.Degraded, ["stop", "pause", "restart"] },
            { ResourceState.Stopped, ["run"] },
            { ResourceState.Unknown, ["run"] }
        };

    [Theory]
    [MemberData(nameof(StateResourceActionCapabilities))]
    public async Task GetResourceOperationCapabilities_ReturnsStateSpecificResourceActionCapabilities(
        ResourceState state,
        string[] expectedExecutableActionIds)
    {
        var resource = CreateResource("target", state);
        var controlPlane = CreateControlPlane([resource]);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync([resource.Id]);

        var capability = Assert.Single(capabilities).Value;
        Assert.True(capability.CanManage);
        Assert.True(capability.CanDelete);
        Assert.Equal(
            expectedExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase),
            capability.ExecutableActionIds.Order(StringComparer.OrdinalIgnoreCase));
        Assert.Equal(
            resource.ResourceActions.Select(action => action.Id).Order(StringComparer.OrdinalIgnoreCase),
            capability.ResourceActionCapabilities.Select(action => action.ActionId).Order(StringComparer.OrdinalIgnoreCase));
        Assert.All(
            capability.ResourceActionCapabilities.Where(action => !action.CanExecute),
            action => Assert.False(string.IsNullOrWhiteSpace(action.Reason)));
    }

    [Theory]
    [InlineData(ResourceState.Running, "run")]
    [InlineData(ResourceState.Stopped, "stop")]
    [InlineData(ResourceState.Paused, "restart")]
    [InlineData(ResourceState.Unknown, "pause")]
    public async Task ExecuteResourceActionAsync_RejectsStateInvalidActions(
        ResourceState state,
        string actionId)
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", state)], provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", actionId)));

        Assert.Contains("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.ExecutedActions);
    }

    [Theory]
    [InlineData(ResourceState.Starting, "restart")]
    [InlineData(ResourceState.Paused, "stop")]
    [InlineData(ResourceState.Unknown, "run")]
    public async Task ExecuteResourceActionAsync_AllowsStateValidActions(
        ResourceState state,
        string actionId)
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", state)], provider);

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", actionId));

        Assert.Equal([$"target:{actionId}"], provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_BlocksStopWhenRunningDependentsExist()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependent", ResourceState.Running, dependsOn: ["target"])
            ],
            provider);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", "stop")));

        Assert.Contains("depend on this resource", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_DoesNotBlockStopForPausedDependents()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependent", ResourceState.Paused, dependsOn: ["target"])
            ],
            provider);

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", "stop"));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    private static IResourceManager CreateControlPlane(
        IReadOnlyList<CloudResource> resources,
        TestResourceProvider? provider = null)
    {
        provider ??= new TestResourceProvider();
        var registrations = new TestResourceRegistrationStore(resources.Select(resource =>
            new ResourceRegistration(
                resource.Id,
                provider.Id,
                null,
                DateTimeOffset.UtcNow,
                resource.DependsOn)));
        var resourceManager = new TestResourceManagerStore(resources, [provider]);
        var resourceGroups = new TestResourceGroupStore();
        var templates = new ResourceTemplateService(resourceManager, resourceGroups, registrations);
        var orchestration = new ResourceOrchestrationService(
            [new DefaultResourceOrchestrator()],
            [],
            [],
            resourceManager,
            registrations,
            new ResourceDeclarationStore(),
            CreateSelectionStore());

        return new InProcessControlPlane(
            resourceManager,
            resourceGroups,
            registrations,
            orchestration,
            templates,
            new EmptyLogStore(),
            new EmptyTraceStore(),
            new AllowAllAuthorizationService());
    }

    private static CloudResource CreateResource(
        string id,
        ResourceState state,
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            id,
            id,
            "Test",
            "Test",
            "local",
            state,
            [],
            "1.0",
            DateTimeOffset.UtcNow,
            dependsOn ?? [],
            Actions:
            [
                ResourceAction.Run,
                ResourceAction.Stop,
                ResourceAction.Pause,
                ResourceAction.Restart
            ]);

    private static ResourceOrchestratorSelectionStore CreateSelectionStore()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);
        return new ResourceOrchestratorSelectionStore(
            new TestHostEnvironment(contentRoot),
            new TestOptionsMonitor<ResourceManagerOptions>(new ResourceManagerOptions()));
    }

    private sealed class TestResourceProvider : IResourceProvider, IResourceProcedureProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public List<string> ExecutedActions { get; } = [];

        public IReadOnlyList<CloudResource> GetResources() => [];

        public Task<ResourceProcedureResult> DeleteAsync(
            ResourceProcedureContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed($"Deleted {context.Resource.Id}."));

        public Task<ResourceProcedureResult> ExecuteActionAsync(
            ResourceProcedureContext context,
            ResourceAction action,
            CancellationToken cancellationToken = default)
        {
            ExecutedActions.Add($"{context.Resource.Id}:{action.Id}");
            return Task.FromResult(ResourceProcedureResult.Completed($"Executed {action.Id}."));
        }
    }

    private sealed class TestResourceManagerStore(
        IReadOnlyList<CloudResource> resources,
        IReadOnlyList<IResourceProvider> providers) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => providers;

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public IReadOnlyList<CloudResource> GetAvailableResources() => resources;

        public IReadOnlyList<CloudResource> GetResources() => resources;

        public CloudResource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<CloudResource> GetChildren(string resourceId) =>
            resources
                .Where(resource => string.Equals(resource.ParentResourceId, resourceId, StringComparison.OrdinalIgnoreCase))
                .ToArray();

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public bool IsRegistered(string resourceId) =>
            resources.Any(resource => string.Equals(resource.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private sealed class TestResourceRegistrationStore(IEnumerable<ResourceRegistration> registrations) :
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
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "test", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                ResourceGroupId = resourceGroupId,
                DependsOn = dependsOn ?? existing.DependsOn
            };
            return Task.CompletedTask;
        }

        public Task SetDependenciesAsync(
            string resourceId,
            IReadOnlyList<string> dependsOn,
            CancellationToken cancellationToken = default)
        {
            var existing = GetRegistration(resourceId) ??
                new ResourceRegistration(resourceId, "test", null, DateTimeOffset.UtcNow, []);
            _registrations[resourceId] = existing with
            {
                DependsOn = dependsOn
            };
            return Task.CompletedTask;
        }
    }

    private sealed class TestResourceGroupStore : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => [];

        public ResourceGroup? GetGroupForResource(string resourceId) => null;

        public Task<ResourceGroup> CreateAsync(
            string name,
            string description,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new ResourceGroup(Guid.NewGuid().ToString("N"), name, description, []));
    }

    private sealed class EmptyLogStore : ILogStore
    {
        public IReadOnlyList<ILogProvider> Providers => [];

        public IReadOnlyList<LogDescriptor> GetLogs() => [];

        public IReadOnlyList<LogDescriptor> GetLogsForResource(string resourceId) => [];

        public LogDescriptor? GetLog(string logId) => null;

        public Task<IReadOnlyList<LogEntry>> ReadLogAsync(
            string logId,
            int maxEntries = 200,
            DateTimeOffset? before = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<LogEntry>>([]);

        public async IAsyncEnumerable<LogEntry> StreamLogAsync(
            string logId,
            int initialEntries = 50,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class EmptyTraceStore : ITraceStore
    {
        public IReadOnlyList<TraceSpan> GetSpans(
            string? resourceId = null,
            string? traceId = null,
            int maxSpans = 200) => [];

        public void AddSpans(IEnumerable<TraceSpan> spans)
        {
        }
    }

    private sealed class AllowAllAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => true;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => true;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => true;
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.ControlPlane.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class TestOptionsMonitor<T>(T value) : IOptionsMonitor<T>
    {
        public T CurrentValue => value;

        public T Get(string? name) => value;

        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }
}
