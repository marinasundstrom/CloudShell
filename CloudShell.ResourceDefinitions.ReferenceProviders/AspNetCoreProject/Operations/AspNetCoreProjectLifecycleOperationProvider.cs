namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectStartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectStartOperationProvider(
        IAspNetCoreProjectRuntimeController? runtimeController = null)
        : base(
            AspNetCoreProjectResourceTypeProvider.Operations.Start,
            runtimeController)
    {
    }
}

public sealed class AspNetCoreProjectRestartOperationProvider :
    AspNetCoreProjectLifecycleOperationProvider
{
    public AspNetCoreProjectRestartOperationProvider(
        IAspNetCoreProjectRuntimeController? runtimeController = null)
        : base(
            AspNetCoreProjectResourceTypeProvider.Operations.Restart,
            runtimeController)
    {
    }
}

public abstract class AspNetCoreProjectLifecycleOperationProvider(
    ResourceOperationId operationId,
    IAspNetCoreProjectRuntimeController? runtimeController = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IAspNetCoreProjectRuntimeController _runtimeController =
        runtimeController ?? new NoopAspNetCoreProjectRuntimeController();

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
                operation,
                _runtimeController));
}

public sealed class AspNetCoreProjectLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IAspNetCoreProjectRuntimeController runtimeController) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IAspNetCoreProjectRuntimeController _runtimeController =
        runtimeController;

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

        var diagnostics = await _runtimeController.ExecuteAsync(
            Resource,
            OperationId,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}
