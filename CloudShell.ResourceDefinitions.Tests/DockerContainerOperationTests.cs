using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

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
