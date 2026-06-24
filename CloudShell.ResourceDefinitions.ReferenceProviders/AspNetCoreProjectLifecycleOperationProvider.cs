namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectStartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectStartOperationProvider()
        : base(AspNetCoreProjectResourceTypeProvider.Operations.Start)
    {
    }
}

public sealed class AspNetCoreProjectRestartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectRestartOperationProvider()
        : base(AspNetCoreProjectResourceTypeProvider.Operations.Restart)
    {
    }
}

public abstract class AspNetCoreProjectLifecycleOperationProvider(
    ResourceOperationId operationId) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId &&
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
            new AspNetCoreProjectLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation));
}

public sealed class AspNetCoreProjectLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

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
                        "application.aspNetCoreProject.operationUnavailable",
                        UnavailableReason ?? $"The '{OperationId}' operation is not available.",
                        OperationId)
                ]);
        }

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            []);
    }
}
