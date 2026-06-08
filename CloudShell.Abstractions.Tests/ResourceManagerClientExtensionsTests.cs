using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceManagerClientExtensionsTests
{
    [Fact]
    public async Task ExecuteResourceActionAsync_ResourceActionOverload_MapsToCommand()
    {
        var resourceManager = new RecordingResourceManager();
        var resource = CreateResource();

        await resourceManager.ExecuteResourceActionAsync(
            resource,
            ResourceAction.Restart,
            startDependencies: true,
            ignoreDependentWarning: true);

        Assert.Equal(
            new ExecuteResourceActionCommand(
                "sample:resource",
                ResourceActionIds.Restart,
                StartDependencies: true,
                IgnoreDependentWarning: true),
            resourceManager.LastCommand);
    }

    [Fact]
    public async Task StandardLifecycleHelpers_MapToCanonicalActionIds()
    {
        var resourceManager = new RecordingResourceManager();
        var resource = CreateResource();

        await resourceManager.RunResourceAsync("sample:resource", startDependencies: true);
        Assert.Equal(ResourceActionIds.Run, resourceManager.LastCommand?.ActionId);
        Assert.True(resourceManager.LastCommand?.StartDependencies);

        await resourceManager.StopResourceAsync("sample:resource", ignoreDependentWarning: true);
        Assert.Equal(ResourceActionIds.Stop, resourceManager.LastCommand?.ActionId);
        Assert.True(resourceManager.LastCommand?.IgnoreDependentWarning);

        await resourceManager.PauseResourceAsync("sample:resource");
        Assert.Equal(ResourceActionIds.Pause, resourceManager.LastCommand?.ActionId);

        await resourceManager.RestartResourceAsync(
            "sample:resource",
            startDependencies: true,
            ignoreDependentWarning: true);
        Assert.Equal(ResourceActionIds.Restart, resourceManager.LastCommand?.ActionId);
        Assert.True(resourceManager.LastCommand?.StartDependencies);
        Assert.True(resourceManager.LastCommand?.IgnoreDependentWarning);

        await resourceManager.RunResourceAsync(resource, startDependencies: true);
        Assert.Equal(ResourceActionIds.Run, resourceManager.LastCommand?.ActionId);
        Assert.Equal(resource.Id, resourceManager.LastCommand?.ResourceId);
        Assert.True(resourceManager.LastCommand?.StartDependencies);

        await resourceManager.StopResourceAsync(resource, ignoreDependentWarning: true);
        Assert.Equal(ResourceActionIds.Stop, resourceManager.LastCommand?.ActionId);
        Assert.Equal(resource.Id, resourceManager.LastCommand?.ResourceId);
        Assert.True(resourceManager.LastCommand?.IgnoreDependentWarning);

        await resourceManager.PauseResourceAsync(resource);
        Assert.Equal(ResourceActionIds.Pause, resourceManager.LastCommand?.ActionId);
        Assert.Equal(resource.Id, resourceManager.LastCommand?.ResourceId);

        await resourceManager.RestartResourceAsync(
            resource,
            startDependencies: true,
            ignoreDependentWarning: true);
        Assert.Equal(ResourceActionIds.Restart, resourceManager.LastCommand?.ActionId);
        Assert.Equal(resource.Id, resourceManager.LastCommand?.ResourceId);
        Assert.True(resourceManager.LastCommand?.StartDependencies);
        Assert.True(resourceManager.LastCommand?.IgnoreDependentWarning);
    }

    [Fact]
    public async Task GetResourceOperationCapabilitiesAsync_SingularOverload_ReturnsMatchingCapabilities()
    {
        var resourceManager = new RecordingResourceManager();
        var capabilities = new ResourceOperationCapabilities(
            "sample:resource",
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ResourceActionIds.Stop
            });
        resourceManager.Capabilities["sample:resource"] = capabilities;

        var result = await resourceManager.GetResourceOperationCapabilitiesAsync("sample:resource");

        Assert.Same(capabilities, result);
        Assert.Equal(["sample:resource"], resourceManager.LastCapabilityRequest);
    }

    [Fact]
    public async Task GetResourceOperationCapabilitiesAsync_ResourceOverload_ReturnsNoneWhenMissing()
    {
        var resourceManager = new RecordingResourceManager();
        var resource = CreateResource();

        var result = await resourceManager.GetResourceOperationCapabilitiesAsync(resource);

        Assert.Equal(resource.Id, result.ResourceId);
        Assert.False(result.CanManage);
        Assert.False(result.CanDelete);
        Assert.Empty(result.ExecutableActionIds);
        Assert.Empty(result.ResourceActionCapabilities);
        Assert.Equal([resource.Id], resourceManager.LastCapabilityRequest);
    }

    private static CloudResource CreateResource() =>
        new(
            "sample:resource",
            "Sample",
            "Sample",
            "Sample",
            "local",
            ResourceState.Running,
            [],
            "1.0.0",
            DateTimeOffset.UnixEpoch,
            [],
            Actions: [ResourceAction.Restart]);

    private sealed class RecordingResourceManager : IResourceManager
    {
        public ExecuteResourceActionCommand? LastCommand { get; private set; }

        public IReadOnlyList<string> LastCapabilityRequest { get; private set; } = [];

        public Dictionary<string, ResourceOperationCapabilities> Capabilities { get; } =
            new(StringComparer.OrdinalIgnoreCase);

        public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceGroup>>([]);

        public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResourceGroup?>(null);

        public Task<ResourceGroup> CreateResourceGroupAsync(
            CreateResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<CloudResource>> ListAvailableResourcesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudResource>>([]);

        public Task<IReadOnlyList<CloudResource>> ListResourcesAsync(
            ResourceQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudResource>>([]);

        public Task<CloudResource?> GetResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<CloudResource?>(null);

        public Task<IReadOnlyList<CloudResource>> ListResourceChildrenAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CloudResource>>([]);

        public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceRegistration>>([]);

        public Task<ResourceRegistration?> GetResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ResourceRegistration?>(null);

        public Task CreateResourceAsync(
            CreateResourceCommand command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
            IReadOnlyList<string> resourceIds,
            CancellationToken cancellationToken = default)
        {
            LastCapabilityRequest = resourceIds;
            return Task.FromResult<IReadOnlyDictionary<string, ResourceOperationCapabilities>>(
                Capabilities
                    .Where(item => resourceIds.Contains(item.Key, StringComparer.OrdinalIgnoreCase))
                    .ToDictionary(
                        item => item.Key,
                        item => item.Value,
                        StringComparer.OrdinalIgnoreCase));
        }

        public Task RegisterResourceAsync(
            RegisterResourceCommand command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RemoveResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AssignResourceGroupAsync(
            AssignResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SetResourceDependenciesAsync(
            SetResourceDependenciesCommand command,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<ResourceProcedureResult> DeleteResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(ResourceProcedureResult.Completed(resourceId));

        public Task<ResourceProcedureResult> ExecuteResourceActionAsync(
            ExecuteResourceActionCommand command,
            CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            return Task.FromResult(ResourceProcedureResult.Completed(command.ActionId));
        }
    }
}
