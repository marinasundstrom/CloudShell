using CloudShell.ControlPlane.Providers;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

namespace CloudShell.ResourceModel.Tests;

public sealed class DockerContainerOperationTests
{
    [Fact]
    public async Task LifecycleOperation_DelegatesToRuntimeHandler()
    {
        var runtimeHandler = new TestDockerContainerRuntimeHandler();
        var startProvider = new DockerContainerStartOperationProvider(runtimeHandler);
        var stopProvider = new DockerContainerStopOperationProvider(runtimeHandler);
        var restartProvider = new DockerContainerRestartOperationProvider(runtimeHandler);
        var pauseProvider = new DockerContainerPauseOperationProvider(runtimeHandler);
        var unpauseProvider = new DockerContainerUnpauseOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(
            startProvider,
            stopProvider,
            restartProvider,
            pauseProvider,
            unpauseProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var startOperation = result.Resource.Operations.Get(
            DockerContainerResourceTypeProvider.Operations.Start) as DockerContainerLifecycleOperation;

        Assert.NotNull(startOperation);

        var execution = await startOperation.ExecuteAsync();

        Assert.False(execution.HasErrors);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(result.Resource, invocation.Resource);
        Assert.Equal(DockerContainerResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task LifecycleOperation_DispatchesLifecycleInstruction()
    {
        var dispatcher = new RecordingExecutionDispatcher();
        var runtimeHandler = new TestDockerContainerRuntimeHandler();
        var startProvider = new DockerContainerStartOperationProvider(runtimeHandler, dispatcher);
        var stopProvider = new DockerContainerStopOperationProvider(runtimeHandler, dispatcher);
        var restartProvider = new DockerContainerRestartOperationProvider(runtimeHandler, dispatcher);
        var pauseProvider = new DockerContainerPauseOperationProvider(runtimeHandler, dispatcher);
        var unpauseProvider = new DockerContainerUnpauseOperationProvider(runtimeHandler, dispatcher);
        var pipeline = CreatePipeline(
            startProvider,
            stopProvider,
            restartProvider,
            pauseProvider,
            unpauseProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var startOperation = result.Resource.Operations.Get(
            DockerContainerResourceTypeProvider.Operations.Start) as DockerContainerLifecycleOperation;

        Assert.NotNull(startOperation);

        await startOperation.ExecuteAsync();

        var request = Assert.Single(dispatcher.Requests);
        Assert.Equal(ProviderExecutionInstructionTypes.ContainerStart, request.InstructionType);
        Assert.Equal(result.Resource.EffectiveResourceId, request.TargetResourceId);
        Assert.Equal(result.Resource.Revision.Value, request.DesiredGeneration);
        Assert.Equal(
            $"{result.Resource.EffectiveResourceId}:{DockerContainerResourceTypeProvider.Operations.Start}:{result.Resource.Revision.Value}",
            request.IdempotencyKey);
        Assert.Contains(ProviderExecutionCapabilities.Containers, request.RequiredCapabilities);
        Assert.Same(result.Resource, request.TargetResourceSnapshot);
        Assert.Equal([result.Resource], request.ResourceSnapshot);
    }

    [Fact]
    public async Task LifecycleExecutionHandler_DelegatesToRuntimeHandler()
    {
        var runtimeHandler = new TestDockerContainerRuntimeHandler();
        var resource = await CreateResourceAsync();
        var handler = new DockerContainerStartExecutionHandler(runtimeHandler);
        var request = new ProviderExecutionRequest
        {
            AssignmentId = $"{resource.EffectiveResourceId}:start",
            InstructionType = ProviderExecutionInstructionTypes.ContainerStart,
            TargetResourceId = resource.EffectiveResourceId,
            DesiredGeneration = resource.Revision.Value,
            IdempotencyKey = $"{resource.EffectiveResourceId}:start:{resource.Revision.Value}",
            TargetResourceSnapshot = resource,
            ResourceSnapshot = [resource]
        };

        var result = await handler.ExecuteAsync(request);

        Assert.Equal(ProviderExecutionStatus.Succeeded, result.Status);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(resource, invocation.Resource);
        Assert.Equal(DockerContainerResourceTypeProvider.Operations.Start, invocation.OperationId);
    }

    [Fact]
    public async Task LifecycleOperation_UsesRuntimeStatusForAvailability()
    {
        var runtimeHandler = new TestDockerContainerRuntimeHandler
        {
            Status = DockerContainerRuntimeStatus.Running
        };
        var startProvider = new DockerContainerStartOperationProvider(runtimeHandler);
        var stopProvider = new DockerContainerStopOperationProvider(runtimeHandler);
        var restartProvider = new DockerContainerRestartOperationProvider(runtimeHandler);
        var pauseProvider = new DockerContainerPauseOperationProvider(runtimeHandler);
        var unpauseProvider = new DockerContainerUnpauseOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(
            startProvider,
            stopProvider,
            restartProvider,
            pauseProvider,
            unpauseProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var startOperation = result.Resource.Operations.Get(
            DockerContainerResourceTypeProvider.Operations.Start) as DockerContainerLifecycleOperation;
        var stopOperation = result.Resource.Operations.Get(
            DockerContainerResourceTypeProvider.Operations.Stop) as DockerContainerLifecycleOperation;
        var pauseOperation = result.Resource.Operations.Get(
            DockerContainerResourceTypeProvider.Operations.Pause) as DockerContainerLifecycleOperation;

        Assert.NotNull(startOperation);
        Assert.NotNull(stopOperation);
        Assert.NotNull(pauseOperation);
        Assert.False(await startOperation.CanExecuteAsync());
        Assert.True(await stopOperation.CanExecuteAsync());
        Assert.True(await pauseOperation.CanExecuteAsync());

        var execution = await startOperation.ExecuteAsync();

        Assert.True(execution.HasErrors);
        Assert.Empty(runtimeHandler.LifecycleInvocations);
    }

    [Fact]
    public async Task ResourceManagerStateProvider_ProjectsRuntimeStatus()
    {
        var runtimeHandler = new TestDockerContainerRuntimeHandler
        {
            Status = DockerContainerRuntimeStatus.Paused
        };
        var pipeline = CreatePipeline(
            new DockerContainerStartOperationProvider(runtimeHandler),
            new DockerContainerStopOperationProvider(runtimeHandler),
            new DockerContainerRestartOperationProvider(runtimeHandler),
            new DockerContainerPauseOperationProvider(runtimeHandler),
            new DockerContainerUnpauseOperationProvider(runtimeHandler));

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var stateProvider = new DockerContainerResourceManagerStateProvider(runtimeHandler);

        Assert.Equal(ResourceManagerState.Paused, stateProvider.GetState(result.Resource));
    }

    private static ResourceDefinitionValidationPipeline CreatePipeline(
        DockerContainerStartOperationProvider startProvider,
        DockerContainerStopOperationProvider stopProvider,
        DockerContainerRestartOperationProvider restartProvider,
        DockerContainerPauseOperationProvider pauseProvider,
        DockerContainerUnpauseOperationProvider unpauseProvider)
    {
        IResourceOperationProvider[] operationProviders =
        [
            startProvider,
            stopProvider,
            restartProvider,
            pauseProvider,
            unpauseProvider
        ];

        return new(
            [DockerContainerResourceTypeProvider.ClassDefinition],
            [new DockerContainerResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
    }

    private static ResourceDefinition CreateDefinition() =>
        new(
            "registry",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] =
                    "registry:2"
            });

    private static async ValueTask<Resource> CreateResourceAsync()
    {
        var pipeline = CreatePipeline(
            new DockerContainerStartOperationProvider(),
            new DockerContainerStopOperationProvider(),
            new DockerContainerRestartOperationProvider(),
            new DockerContainerPauseOperationProvider(),
            new DockerContainerUnpauseOperationProvider());
        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        return result.Resource;
    }

    private sealed class RecordingExecutionDispatcher : IProviderExecutionDispatcher
    {
        public List<ProviderExecutionRequest> Requests { get; } = [];

        public ValueTask<ProviderExecutionResult> ExecuteAsync(
            ProviderExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            return ValueTask.FromResult(ProviderExecutionResult.Succeeded(request));
        }
    }

    private sealed class TestDockerContainerRuntimeHandler :
        IDockerContainerRuntimeHandler
    {
        public DockerContainerRuntimeStatus Status { get; init; } =
            DockerContainerRuntimeStatus.Unknown;

        public List<(Resource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public DockerContainerRuntimeStatus GetStatus(Resource resource) =>
            Status;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }
}
