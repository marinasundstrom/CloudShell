using CloudShell.ResourceModel.ReferenceProviders;

namespace CloudShell.ResourceModel.Tests;

public sealed class ContainerApplicationOperationTests
{
    [Fact]
    public async Task LifecycleOperation_DelegatesToRuntimeHandler()
    {
        var runtimeHandler = new TestContainerApplicationRuntimeHandler();
        var startProvider = new ContainerApplicationStartOperationProvider(runtimeHandler);
        var stopProvider = new ContainerApplicationStopOperationProvider(runtimeHandler);
        var restartProvider = new ContainerApplicationRestartOperationProvider(runtimeHandler);
        var imageProvider = new ContainerApplicationImageUpdateOperationProvider(runtimeHandler);
        var replicasProvider = new ContainerApplicationReplicasUpdateOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(startProvider, stopProvider, restartProvider, imageProvider, replicasProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var startOperation = result.Resource.Operations.Get(
            ContainerApplicationResourceTypeProvider.Operations.Start) as ContainerApplicationLifecycleOperation;

        Assert.NotNull(startOperation);

        var execution = await startOperation.ExecuteAsync();

        Assert.False(execution.HasErrors);
        var invocation = Assert.Single(runtimeHandler.LifecycleInvocations);
        Assert.Same(result.Resource, invocation.Resource);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Start, invocation.OperationId);

        var stopOperation = result.Resource.Operations.Get(
            ContainerApplicationResourceTypeProvider.Operations.Stop) as ContainerApplicationLifecycleOperation;

        Assert.NotNull(stopOperation);

        execution = await stopOperation.ExecuteAsync();

        Assert.False(execution.HasErrors);
        Assert.Equal(2, runtimeHandler.LifecycleInvocations.Count);
        invocation = runtimeHandler.LifecycleInvocations[1];
        Assert.Same(result.Resource, invocation.Resource);
        Assert.Equal(ContainerApplicationResourceTypeProvider.Operations.Stop, invocation.OperationId);
    }

    [Fact]
    public async Task ImageUpdateOperation_UpdatesGraphStateAndDelegatesRuntimeApply()
    {
        var runtimeHandler = new TestContainerApplicationRuntimeHandler();
        var startProvider = new ContainerApplicationStartOperationProvider(runtimeHandler);
        var stopProvider = new ContainerApplicationStopOperationProvider(runtimeHandler);
        var restartProvider = new ContainerApplicationRestartOperationProvider(runtimeHandler);
        var imageProvider = new ContainerApplicationImageUpdateOperationProvider(runtimeHandler);
        var replicasProvider = new ContainerApplicationReplicasUpdateOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(startProvider, stopProvider, restartProvider, imageProvider, replicasProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var imageOperation = result.Resource.Operations.Get(
            ContainerApplicationResourceTypeProvider.Operations.UpdateImage) as ContainerApplicationImageUpdateOperation;

        Assert.NotNull(imageOperation);

        var changes = imageOperation.UpdateImage("registry.test/api:2", replicas: 3);
        var execution = await imageOperation.ExecuteAsync();

        Assert.True(changes.HasChanges);
        Assert.Equal(
            "registry.test/api:2",
            changes.ProposedState.ResourceAttributes[
                ContainerApplicationResourceTypeProvider.Attributes.ContainerImage]);
        Assert.Equal(
            "3",
            changes.ProposedState.ResourceAttributes[
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas]);
        Assert.False(execution.HasErrors);
        Assert.Same(result.Resource, Assert.Single(runtimeHandler.ImageApplyInvocations));
    }

    [Fact]
    public async Task ReplicasUpdateOperation_UpdatesGraphStateAndDelegatesRuntimeApply()
    {
        var runtimeHandler = new TestContainerApplicationRuntimeHandler();
        var startProvider = new ContainerApplicationStartOperationProvider(runtimeHandler);
        var stopProvider = new ContainerApplicationStopOperationProvider(runtimeHandler);
        var restartProvider = new ContainerApplicationRestartOperationProvider(runtimeHandler);
        var imageProvider = new ContainerApplicationImageUpdateOperationProvider(runtimeHandler);
        var replicasProvider = new ContainerApplicationReplicasUpdateOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(startProvider, stopProvider, restartProvider, imageProvider, replicasProvider);

        var result = await pipeline.ValidateAsync(
            CreateDefinition(),
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);

        var replicasOperation = result.Resource.Operations.Get(
            ContainerApplicationResourceTypeProvider.Operations.UpdateReplicas) as ContainerApplicationReplicasUpdateOperation;

        Assert.NotNull(replicasOperation);

        var changes = replicasOperation.UpdateReplicas(4);
        var execution = await replicasOperation.ExecuteAsync();

        Assert.True(changes.HasChanges);
        Assert.Equal(
            "4",
            changes.ProposedState.ResourceAttributes[
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas]);
        Assert.False(execution.HasErrors);
        Assert.Same(result.Resource, Assert.Single(runtimeHandler.ReplicasApplyInvocations));
        Assert.Empty(runtimeHandler.ImageApplyInvocations);
    }

    private static ResourceDefinitionValidationPipeline CreatePipeline(
        ContainerApplicationStartOperationProvider startProvider,
        ContainerApplicationStopOperationProvider stopProvider,
        ContainerApplicationRestartOperationProvider restartProvider,
        ContainerApplicationImageUpdateOperationProvider imageProvider,
        ContainerApplicationReplicasUpdateOperationProvider replicasProvider)
    {
        IResourceOperationProvider[] operationProviders =
        [
            startProvider,
            stopProvider,
            restartProvider,
            imageProvider,
            replicasProvider
        ];

        return new(
            [ContainerApplicationResourceTypeProvider.ClassDefinition],
            [new ContainerApplicationResourceTypeProvider()],
            operationProviders: operationProviders,
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());
    }

    private static ResourceDefinition CreateDefinition() =>
        new(
            "api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "registry.test/api:1",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] =
                    2
            });

    private sealed class TestContainerApplicationRuntimeHandler :
        IContainerApplicationRuntimeHandler
    {
        public List<(Resource Resource, ResourceOperationId OperationId)> LifecycleInvocations { get; } = [];

        public List<Resource> ImageApplyInvocations { get; } = [];

        public List<Resource> ReplicasApplyInvocations { get; } = [];

        public ContainerApplicationRuntimeStatus GetStatus(Resource resource) =>
            ContainerApplicationRuntimeStatus.Unknown;

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ExecuteLifecycleAsync(
            Resource resource,
            ResourceOperationId operationId,
            CancellationToken cancellationToken = default)
        {
            LifecycleInvocations.Add((resource, operationId));
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyImageAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            ImageApplyInvocations.Add(resource);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }

        public ValueTask<IReadOnlyList<ResourceDefinitionDiagnostic>> ApplyReplicasAsync(
            Resource resource,
            CancellationToken cancellationToken = default)
        {
            ReplicasApplyInvocations.Add(resource);
            return ValueTask.FromResult<IReadOnlyList<ResourceDefinitionDiagnostic>>([]);
        }
    }
}
