using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.ControlPlane.Tests;

public sealed class InProcessControlPlaneResourceStateTests
{
    public static TheoryData<ResourceState, string[]> StateResourceActionCapabilities =>
        new()
        {
            { ResourceState.Running, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Starting, [ResourceActionIds.Stop, ResourceActionIds.Restart] },
            { ResourceState.Paused, [ResourceActionIds.Run, ResourceActionIds.Stop] },
            { ResourceState.Degraded, [ResourceActionIds.Stop, ResourceActionIds.Pause, ResourceActionIds.Restart] },
            { ResourceState.Stopped, [ResourceActionIds.Run] },
            { ResourceState.Unknown, [ResourceActionIds.Run] }
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
    [InlineData(ResourceState.Running, ResourceActionIds.Run)]
    [InlineData(ResourceState.Stopped, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Paused, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Unknown, ResourceActionIds.Pause)]
    public async Task ExecuteResourceActionAsync_RejectsStateInvalidActions(
        ResourceState state,
        string actionId)
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane([CreateResource("target", state)], provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", actionId)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnavailable, exception.Error.Code);
        Assert.Contains("cannot", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Empty(provider.ExecutedActions);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("missing", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnknownAction()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", "missing")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionNotFound, exception.Error.Code);
        Assert.Equal("Resource 'target' does not expose action 'missing'.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsUnsupportedProviderActions()
    {
        var provider = new TestReadOnlyResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support actions.", exception.Message);
    }

    [Fact]
    public async Task ExecuteResourceActionAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal("The 'resources.manage' permission is required for resource 'target'.", exception.Message);
        Assert.Empty(provider.ExecutedActions);
    }

    [Theory]
    [InlineData(ResourceState.Starting, ResourceActionIds.Restart)]
    [InlineData(ResourceState.Paused, ResourceActionIds.Stop)]
    [InlineData(ResourceState.Unknown, ResourceActionIds.Run)]
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

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop)));

        Assert.Equal(ControlPlaneErrorCodes.DependentResourcesRunning, exception.Error.Code);
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

        await controlPlane.ExecuteResourceActionAsync(new ExecuteResourceActionCommand("target", ResourceActionIds.Stop));

        Assert.Equal(["target:stop"], provider.ExecutedActions);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.DeleteResourceAsync("missing"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotRegistered, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsDeniedManagePermission()
    {
        var provider = new TestResourceProvider();
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            provider,
            authorization: new DenyAuthorizationService());

        var exception = await Assert.ThrowsAsync<ControlPlaneAccessDeniedException>(() =>
            controlPlane.DeleteResourceAsync("target"));

        Assert.Equal(ControlPlaneErrorCodes.InsufficientPermission, exception.Error.Code);
        Assert.Equal("The 'resources.manage' permission is required for resource 'target'.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_RejectsUnsupportedProviderDelete()
    {
        var controlPlane = CreateControlPlane(
            [CreateResource("target", ResourceState.Running)],
            new TestReadOnlyResourceProvider());

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.DeleteResourceAsync("target"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceDeleteUnsupported, exception.Error.Code);
        Assert.Equal("Resource 'target' does not support delete.", exception.Message);
    }

    [Fact]
    public async Task DeleteResourceAsync_ReturnsProviderResult()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var result = await controlPlane.DeleteResourceAsync("target");

        Assert.Equal("Deleted target.", result.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownProvider()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("missing", "target")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceProviderNotFound, exception.Error.Code);
        Assert.Equal("Resource provider 'missing' is not registered.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownResource()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("test", "missing")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotAvailable, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not available.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsUnknownResourceGroup()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(new RegisterResourceCommand("test", "target", "missing-group")));

        Assert.Equal(ControlPlaneErrorCodes.ResourceGroupNotFound, exception.Error.Code);
        Assert.Equal("Resource group 'missing-group' could not be found.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_RejectsSelfDependencies()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.RegisterResourceAsync(
                new RegisterResourceCommand("test", "target", DependsOn: [" TARGET "])));

        Assert.Equal(ControlPlaneErrorCodes.ResourceSelfDependency, exception.Error.Code);
        Assert.Equal("Resource 'target' cannot depend on itself.", exception.Message);
    }

    [Fact]
    public async Task RegisterResourceAsync_NormalizesDependencies()
    {
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependency", ResourceState.Running)
            ]);

        await controlPlane.RegisterResourceAsync(
            new RegisterResourceCommand("test", "target", DependsOn: [" dependency ", "DEPENDENCY"]));

        var registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.Equal(["dependency"], registration.DependsOn);
    }

    [Fact]
    public async Task AssignResourceGroupAsync_NormalizesGroupAndDependencies()
    {
        var group = new ResourceGroup("group-one", "Group One", "Test group", []);
        var controlPlane = CreateControlPlane(
            [
                CreateResource("target", ResourceState.Running),
                CreateResource("dependency", ResourceState.Running)
            ],
            groups: [group]);

        await controlPlane.AssignResourceGroupAsync(
            new AssignResourceGroupCommand(" target ", " group-one ", [" dependency ", "DEPENDENCY"]));

        var registration = await controlPlane.GetResourceRegistrationAsync("target");
        Assert.NotNull(registration);
        Assert.Equal("group-one", registration.ResourceGroupId);
        Assert.Equal(["dependency"], registration.DependsOn);
    }

    [Fact]
    public async Task SetResourceDependenciesAsync_RejectsUnknownDependencies()
    {
        var controlPlane = CreateControlPlane([CreateResource("target", ResourceState.Running)]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.SetResourceDependenciesAsync(
                new SetResourceDependenciesCommand("target", ["missing"])));

        Assert.Equal(ControlPlaneErrorCodes.ResourceNotAvailable, exception.Error.Code);
        Assert.Equal("Resource 'missing' is not available.", exception.Message);
    }

    [Fact]
    public async Task CreateResourceAsync_RejectsMissingConfiguration()
    {
        var controlPlane = CreateControlPlane([]);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.CreateResourceAsync(
                new CreateResourceCommand("test", "test.resource", "target", "Target", default)));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Equal("Configuration is required.", exception.Message);
    }

    private static IResourceManager CreateControlPlane(
        IReadOnlyList<Resource> resources,
        IResourceProvider? provider = null,
        IReadOnlyList<ResourceGroup>? groups = null,
        ICloudShellAuthorizationService? authorization = null)
    {
        provider ??= new TestResourceProvider();
        var registrations = new TestResourceRegistrationStore(resources.Select(resource =>
            new ResourceRegistration(
                resource.Id,
                provider.Id,
                null,
                DateTimeOffset.UtcNow,
                resource.DependsOn)));
        var resourceManager = new TestResourceManagerStore(resources, [provider], groups ?? []);
        var resourceGroups = new TestResourceGroupStore(groups ?? []);
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
            authorization ?? new AllowAllAuthorizationService());
    }

    private static Resource CreateResource(
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

        public IReadOnlyList<Resource> GetResources() => [];

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

    private sealed class TestReadOnlyResourceProvider : IResourceProvider
    {
        public string Id => "test";

        public string DisplayName => "Test";

        public IReadOnlyList<Resource> GetResources() => [];
    }

    private sealed class TestResourceManagerStore(
        IReadOnlyList<Resource> resources,
        IReadOnlyList<IResourceProvider> providers,
        IReadOnlyList<ResourceGroup> groups) : IResourceManagerStore
    {
        public IReadOnlyList<IResourceProvider> Providers => providers;

        public IReadOnlyList<ResourceGroup> GetResourceGroups() => groups;

        public IReadOnlyList<Resource> GetAvailableResources() => resources;

        public IReadOnlyList<Resource> GetResources() => resources;

        public Resource? GetResource(string id) =>
            resources.FirstOrDefault(resource => string.Equals(resource.Id, id, StringComparison.OrdinalIgnoreCase));

        public IReadOnlyList<Resource> GetChildren(string resourceId) =>
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

    private sealed class TestResourceGroupStore(IReadOnlyList<ResourceGroup> groups) : IResourceGroupStore
    {
        public IReadOnlyList<ResourceGroup> GetResourceGroups() => groups;

        public ResourceGroup? GetGroupForResource(string resourceId) =>
            groups.FirstOrDefault(group =>
                group.ResourceIds.Contains(resourceId, StringComparer.OrdinalIgnoreCase));

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

    private sealed class DenyAuthorizationService : ICloudShellAuthorizationService
    {
        public bool IsAuthenticated => true;

        public bool HasPermission(string permission) => false;

        public bool CanAccessResourceGroup(string? resourceGroupId, string permission) => false;

        public bool CanAccessResource(string resourceId, string? resourceGroupId, string permission) => false;
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
