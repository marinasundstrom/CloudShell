namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class ContainerApplicationImageUpdateOperationProvider(
    IContainerApplicationRuntimeHandler? runtimeHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();

    public ResourceOperationId OperationId =>
        ContainerApplicationResourceTypeProvider.Operations.UpdateImage;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId &&
        operation.IsAvailable;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.Success);

    public bool CanProject(
        Resource resource,
        ResourceOperationResolution operation) =>
        CanHandle(resource, operation);

    public ValueTask<IResourceOperationProjection> ProjectAsync(
        Resource resource,
        ResourceOperationResolution operation,
        ResourceOperationProjectionContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult<IResourceOperationProjection>(
            new ContainerApplicationImageUpdateOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeHandler));
}

public sealed class ContainerApplicationImageUpdateOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IContainerApplicationRuntimeHandler runtimeHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IContainerApplicationRuntimeHandler _runtimeHandler = runtimeHandler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => ContainerApplicationResourceTypeProvider.Operations.UpdateImage;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ResourceChangeSet UpdateImage(
        string image,
        int? replicas = null)
    {
        using var changes = Context.CreateChangeContext();
        changes.SetAttribute(
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage,
            image);

        if (replicas is not null)
        {
            changes.SetAttribute(
                ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas,
                replicas.Value);
        }

        return changes.ApplyChanges();
    }

    public async ValueTask<ResourceOperationExecutionResult> ExecuteAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await CanExecuteAsync(cancellationToken))
        {
            return new ResourceOperationExecutionResult(
                Resource,
                OperationId,
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.container.imageUpdateUnavailable",
                        UnavailableReason ?? "The container image update operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _runtimeHandler.ApplyImageAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}
