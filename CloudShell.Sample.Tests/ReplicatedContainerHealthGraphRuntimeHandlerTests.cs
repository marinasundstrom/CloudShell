using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection;
using GraphResource = CloudShell.ResourceDefinitions.Resource;

namespace CloudShell.Sample.Tests;

public sealed class ReplicatedContainerHealthGraphRuntimeHandlerTests
{
    [Theory]
    [InlineData(false, ContainerApplicationRuntimeStatus.Stopped)]
    [InlineData(true, ContainerApplicationRuntimeStatus.Running)]
    public async Task ResourceManagerBridge_MapsRuntimeAppRunningState(
        bool isRunning,
        ContainerApplicationRuntimeStatus expectedStatus)
    {
        var bridge = CreateResourceManagerBridge(
            new RecordingResourceManager(),
            new RecordingRunningState { IsRunningResult = isRunning });

        var status = bridge.GetStatus(await CreateGraphAppResourceAsync());

        Assert.Equal(expectedStatus, status);
    }

    [Theory]
    [InlineData("start", "start", true, false)]
    [InlineData("stop", "stop", false, true)]
    [InlineData("restart", "restart", true, true)]
    public async Task ResourceManagerBridge_DelegatesLifecycleToRuntimeApp(
        string graphOperationId,
        string expectedActionId,
        bool expectedStartDependencies,
        bool expectedIgnoreDependentWarning)
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ExecuteLifecycleAsync(
            await CreateGraphAppResourceAsync(),
            graphOperationId);

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ActionCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal(expectedActionId, command.ActionId);
        Assert.Equal(expectedStartDependencies, command.StartDependencies);
        Assert.Equal(expectedIgnoreDependentWarning, command.IgnoreDependentWarning);
    }

    [Fact]
    public async Task ResourceManagerBridge_DelegatesImageAndReplicasToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ApplyImageAsync(
            await CreateGraphAppResourceAsync(
                image: "cloudshell-application-api:20260622.3",
                replicas: 5));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ImageCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal("cloudshell-application-api:20260622.3", command.Image);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
        Assert.Equal(5, command.RequestedReplicas);
    }

    [Fact]
    public async Task ResourceManagerBridge_DelegatesReplicaUpdateToRuntimeApp()
    {
        var resourceManager = new RecordingResourceManager();
        var bridge = CreateResourceManagerBridge(resourceManager, new RecordingRunningState());

        var diagnostics = await bridge.ApplyReplicasAsync(
            await CreateGraphAppResourceAsync(replicas: 2));

        Assert.Empty(diagnostics);
        var command = Assert.Single(resourceManager.ReplicaCommands);
        Assert.Equal("application:api", command.ResourceId);
        Assert.Equal(2, command.Replicas);
        Assert.False(command.RestartIfRunning);
        Assert.Equal("resource-graph", command.TriggeredBy);
    }

    [Fact]
    public async Task Handler_DelegatesMappedGraphApiToBridge()
    {
        var bridge = new RecordingGraphContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ReplicatedContainerHealthGraphRuntimeHandler(bridge);
        var resource = await CreateGraphAppResourceAsync();

        Assert.Equal(ContainerApplicationRuntimeStatus.Running, handler.GetStatus(resource));

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        var command = Assert.Single(bridge.LifecycleCommands);
        Assert.Equal("application.container-app:graph-api", command.Resource.EffectiveResourceId);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, command.OperationId);
    }

    [Fact]
    public async Task Handler_IgnoresUnmappedGraphContainerAppWithoutCallingBridge()
    {
        var bridge = new RecordingGraphContainerAppRuntimeBridge(ContainerApplicationRuntimeStatus.Running);
        var handler = new ReplicatedContainerHealthGraphRuntimeHandler(bridge);
        var resource = await CreateGraphAppResourceAsync(
            name: "other",
            resourceId: "application.container-app:other");

        var diagnostics = await handler.ExecuteLifecycleAsync(
            resource,
            ContainerApplicationResourceTypeProvider.Operations.Start);

        Assert.Empty(diagnostics);
        Assert.Empty(bridge.LifecycleCommands);
        Assert.Equal(ContainerApplicationRuntimeStatus.Unknown, handler.GetStatus(resource));
    }

    private static ReplicatedContainerHealthGraphResourceManagerBridge CreateResourceManagerBridge(
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
        string name = "graph-api",
        string resourceId = "application.container-app:graph-api",
        string image = "cloudshell-application-api:20260622.2",
        int replicas = 3)
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
                name,
                ContainerApplicationResourceTypeProvider.ResourceTypeId,
                ResourceId: resourceId,
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
            Assert.Equal("application:api", applicationId);
            return IsRunningResult;
        }
    }

    private sealed class RecordingGraphContainerAppRuntimeBridge(
        ContainerApplicationRuntimeStatus status) : IReplicatedContainerHealthGraphContainerAppRuntimeBridge
    {
        public List<LifecycleCommand> LifecycleCommands { get; } = [];

        public ContainerApplicationRuntimeStatus GetStatus(GraphResource resource) => status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            GraphResource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleCommands.Add(new(resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            GraphResource resource,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
    }

    private sealed record LifecycleCommand(
        GraphResource Resource,
        ResourceOperationId OperationId);
}
