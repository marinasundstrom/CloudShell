using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

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
        var resourceManager = new RecordingResourceManager();
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
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IHostEnvironment>(new TestHostEnvironment())
            .AddScoped<IResourceManager>(_ => resourceManager)
            .AddScoped<IResourceOrchestrationCatalog>(_ => catalog)
            .AddSingleton<HostScopedResourceShutdownService>()
            .BuildServiceProvider();

        await services.GetRequiredService<HostScopedResourceShutdownService>()
            .StopAsync(CancellationToken.None);

        Assert.Equal(["api", "vault"], resourceManager.Commands.Select(command => command.ResourceId));
        Assert.All(resourceManager.Commands, command =>
        {
            Assert.Equal(ResourceActionIds.Stop, command.ActionId);
            Assert.False(command.StartDependencies);
            Assert.True(command.IgnoreDependentWarning);
            Assert.Equal(HostScopedResourceShutdownService.ShutdownTrigger, command.TriggeredBy);
        });
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

    private sealed class RecordingResourceManager : IResourceManager
    {
        public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

        public List<ExecuteResourceActionCommand> Commands { get; } = [];

        public Task<ResourceProcedureResult> ExecuteResourceActionAsync(
            ExecuteResourceActionCommand command,
            CancellationToken cancellationToken = default)
        {
            Commands.Add(command);
            ResourcesChanged?.Invoke(
                this,
                new ResourceChangeNotification(
                    ResourceChangeKind.ResourceActionExecuted,
                    command.ResourceId,
                    command.ActionId,
                    [command.ResourceId]));
            return Task.FromResult(ResourceProcedureResult.Completed($"Stopped {command.ResourceId}."));
        }

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

        public Task<IReadOnlyList<Resource>> ListAvailableResourcesAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Resource>>([]);

        public Task<IReadOnlyList<Resource>> ListResourcesAsync(
            ResourceQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Resource>>([]);

        public Task<Resource?> GetResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<Resource?>(null);

        public Task<IReadOnlyList<Resource>> ListResourceChildrenAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Resource>>([]);

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
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
            IReadOnlyList<string> resourceIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyDictionary<string, ResourceOperationCapabilities>>(
                new Dictionary<string, ResourceOperationCapabilities>(StringComparer.OrdinalIgnoreCase));

        public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
            ResourcePermissionGrantQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourcePermissionGrant>>([]);

        public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference identity,
            string targetResourceId,
            string permission,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RegisterResourceAsync(
            RegisterResourceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AssignResourceGroupAsync(
            AssignResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetResourceDependenciesAsync(
            SetResourceDependenciesCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> DeleteResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> UpdateResourceImageAsync(
            UpdateResourceImageCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
            UpdateResourceReplicasCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
