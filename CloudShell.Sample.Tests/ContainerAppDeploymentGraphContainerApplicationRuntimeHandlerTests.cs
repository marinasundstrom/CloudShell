using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;
using ResourceManagerResource = CloudShell.Abstractions.ResourceManager.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ContainerAppDeploymentGraphContainerApplicationRuntimeHandlerTests
{
    [Theory]
    [InlineData(false, ContainerApplicationRuntimeStatus.Stopped)]
    [InlineData(true, ContainerApplicationRuntimeStatus.Running)]
    public async Task GetStatus_MapsRuntimeAppRunningState(
        bool isRunning,
        ContainerApplicationRuntimeStatus expectedStatus)
    {
        var handler = CreateHandler(
            new RecordingResourceManager(),
            new RecordingRunningState { IsRunningResult = isRunning });

        var status = handler.GetStatus(await CreateGraphAppResourceAsync());

        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("start", "start", true, false)]
    [InlineData("stop", "stop", false, true)]
    [InlineData("restart", "restart", true, true)]
    public async Task ExecuteLifecycle_DelegatesToRuntimeApp(
        string graphOperationId,
        string expectedActionId,
        bool expectedStartDependencies,
        bool expectedIgnoreDependentWarning)
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ExecuteLifecycleAsync(
            await CreateGraphAppResourceAsync(),
            graphOperationId);

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ActionCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal(expectedActionId, command.ActionId);
        Assert.Equal(expectedStartDependencies, command.StartDependencies);
        Assert.Equal(expectedIgnoreDependentWarning, command.IgnoreDependentWarning);
    }

    [Fact]
    public async Task ApplyImage_DelegatesImageAndReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ApplyImageAsync(
            await CreateGraphAppResourceAsync(
                image: "cloudshell/mock-api:20260608.4",
                replicas: 5));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ImageCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal("cloudshell/mock-api:20260608.4", command.Image);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
        Assert.Equal(5, command.RequestedReplicas);
    }

    [Fact]
    public async Task ApplyReplicas_DelegatesReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var handler = CreateHandler(resourceManager, new RecordingRunningState());

        var diagnostics = await handler.ApplyReplicasAsync(
            await CreateGraphAppResourceAsync(replicas: 4));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ReplicaCommands);
        Assert.Equal("application:sample-api", command.ResourceId);
        Assert.Equal(4, command.Replicas);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
    }

    private static ContainerAppDeploymentGraphContainerApplicationRuntimeHandler CreateHandler(
        IResourceManager resourceManager,
        IApplicationResourceRunningStateOperations runningState)
    {
        var services = new ServiceCollection();
        services.AddSingleton(resourceManager);
        services.AddSingleton(runningState);
        var serviceProvider = services.BuildServiceProvider();
        return new(serviceProvider.GetRequiredService<IServiceScopeFactory>());
    }

    private static async Task<GraphResource> CreateGraphAppResourceAsync(
        string image = "cloudshell/mock-api:20260608.1",
        int replicas = 2)
    {
        IResourceOperationProvider[] operationProviders =
        [
            new ContainerApplicationStartOperationProvider(),
            new ContainerApplicationStopOperationProvider(),
            new ContainerApplicationRestartOperationProvider(),
            new ContainerApplicationImageUpdateOperationProvider(),
            new ContainerApplicationReplicasUpdateOperationProvider()
        ];
        var pipeline = new ResourceDefinitionValidationPipeline(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
        var result = await pipeline.ValidateAsync(
            new ResourceDefinition(
                "graph-sample-api",
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: "application.container-app:graph-sample-api",
                Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                {
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image,
                    [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] = replicas
                }),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(
            result.HasErrors,
            string.Join(
                Environment.NewLine,
                result.Diagnostics.Select(diagnostic =>
                    $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message}")));
        return result.Resource;
    }

    private sealed class RecordingRunningState : IApplicationResourceRunningStateOperations
    {
        public bool IsRunningResult { get; init; }

        public bool IsRunning(string applicationId)
        {
            Assert.Equal("application:sample-api", applicationId);
            return IsRunningResult;
        }
    }

    private sealed class RecordingResourceManager : IResourceManager
    {
        public event EventHandler<ResourceChangeNotification>? ResourcesChanged;

        public List<ExecuteResourceActionCommand> ActionCommands { get; } = [];

        public List<UpdateResourceImageCommand> ImageCommands { get; } = [];

        public List<UpdateResourceReplicasCommand> ReplicaCommands { get; } = [];

        public Task<ResourceProcedureResult> ExecuteResourceActionAsync(
            ExecuteResourceActionCommand command,
            CancellationToken cancellationToken = default)
        {
            ActionCommands.Add(command);
            ResourcesChanged?.Invoke(
                this,
                new ResourceChangeNotification(
                    ResourceChangeKind.ResourceActionExecuted,
                    command.ResourceId));
            return Task.FromResult(ResourceProcedureResult.Completed("executed"));
        }

        public Task<ResourceProcedureResult> UpdateResourceImageAsync(
            UpdateResourceImageCommand command,
            CancellationToken cancellationToken = default)
        {
            ImageCommands.Add(command);
            return Task.FromResult(ResourceProcedureResult.Completed("image updated"));
        }

        public Task<ResourceProcedureResult> UpdateResourceReplicasAsync(
            UpdateResourceReplicasCommand command,
            CancellationToken cancellationToken = default)
        {
            ReplicaCommands.Add(command);
            return Task.FromResult(ResourceProcedureResult.Completed("replicas updated"));
        }

        public Task AssignResourceGroupAsync(
            AssignResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task CreateResourceAsync(
            CreateResourceCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceGroup> CreateResourceGroupAsync(
            CreateResourceGroupCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceProcedureResult> DeleteResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourcePermissionEvaluation> EvaluateResourcePermissionGrantAsync(
            ResourceIdentityReference identity,
            string targetResourceId,
            string permission,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceGroup?> GetResourceGroupForResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningStatusResult> GetResourceIdentityProvisioningStatusAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<string, ResourceOperationCapabilities>> GetResourceOperationCapabilitiesAsync(
            IReadOnlyList<string> resourceIds,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceManagerResource?> GetResourceAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceRegistration?> GetResourceRegistrationAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task GrantResourcePermissionAsync(
            GrantResourcePermissionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListAvailableResourcesAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListResourceChildrenAsync(
            string resourceId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceGroup>> ListResourceGroupsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePermissionGrant>> ListResourcePermissionGrantsAsync(
            ResourcePermissionGrantQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePermissionGrantStatus>> ListResourcePermissionGrantStatusesAsync(
            ResourcePermissionGrantQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourcePrincipal>> QueryResourcePrincipalsAsync(
            ResourcePrincipalQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceRegistration>> ListResourceRegistrationsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ResourceManagerResource>> ListResourcesAsync(
            ResourceQuery? query = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProvisioningResult> ProvisionResourceIdentityAsync(
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

        public Task RevokeResourcePermissionAsync(
            RevokeResourcePermissionCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetResourceDependenciesAsync(
            SetResourceDependenciesCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SetResourceIdentityAsync(
            SetResourceIdentityCommand command,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ResourceIdentityProviderSetupResult> SetupResourceIdentityProviderAsync(
            string providerId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
