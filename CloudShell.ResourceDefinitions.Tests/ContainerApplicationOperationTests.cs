using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ContainerApplicationOperationTests
{
    [Fact]
    public async Task LifecycleOperation_DelegatesToRuntimeHandler()
    {
        var runtimeHandler = new TestContainerApplicationRuntimeHandler();
        var startProvider = new ContainerApplicationStartOperationProvider(runtimeHandler);
        var restartProvider = new ContainerApplicationRestartOperationProvider(runtimeHandler);
        var imageProvider = new ContainerApplicationImageUpdateOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(startProvider, restartProvider, imageProvider);

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
    }

    [Fact]
    public async Task ImageUpdateOperation_UpdatesGraphStateAndDelegatesRuntimeApply()
    {
        var runtimeHandler = new TestContainerApplicationRuntimeHandler();
        var startProvider = new ContainerApplicationStartOperationProvider(runtimeHandler);
        var restartProvider = new ContainerApplicationRestartOperationProvider(runtimeHandler);
        var imageProvider = new ContainerApplicationImageUpdateOperationProvider(runtimeHandler);
        var pipeline = CreatePipeline(startProvider, restartProvider, imageProvider);

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

    private static ResourceDefinitionValidationPipeline CreatePipeline(
        ContainerApplicationStartOperationProvider startProvider,
        ContainerApplicationRestartOperationProvider restartProvider,
        ContainerApplicationImageUpdateOperationProvider imageProvider)
    {
        IResourceOperationProvider[] operationProviders =
        [
            startProvider,
            restartProvider,
            imageProvider
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
    }
}
