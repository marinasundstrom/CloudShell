namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class ContainerApplicationStartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationStartOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Start,
            runtimeHandler)
    {
    }
}

public sealed class ContainerApplicationRestartOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationRestartOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Restart,
            runtimeHandler)
    {
    }
}

public sealed class ContainerApplicationStopOperationProvider :
    ContainerApplicationLifecycleOperationProvider
{
    public ContainerApplicationStopOperationProvider(
        IContainerApplicationRuntimeHandler? runtimeHandler = null)
        : base(
            ContainerApplicationResourceTypeProvider.Operations.Stop,
            runtimeHandler)
    {
    }
}

public abstract class ContainerApplicationLifecycleOperationProvider(
    ResourceOperationId operationId,
    IContainerApplicationRuntimeHandler? runtimeHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IContainerApplicationRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopContainerApplicationRuntimeHandler();

    public ResourceOperationId OperationId { get; } = operationId;

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
            new ContainerApplicationLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeHandler));
}

public sealed class ContainerApplicationLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IContainerApplicationRuntimeHandler runtimeHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IContainerApplicationRuntimeHandler _runtimeHandler = runtimeHandler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

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
                        "application.container.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _runtimeHandler.ExecuteLifecycleAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}
