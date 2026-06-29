namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class SqlServerStartOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerStartOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Start,
            runtimeHandler)
    {
    }
}

public sealed class SqlServerStopOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerStopOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Stop,
            runtimeHandler)
    {
    }
}

public sealed class SqlServerRestartOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerRestartOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Restart,
            runtimeHandler)
    {
    }
}

public abstract class SqlServerLifecycleOperationProvider(
    ResourceOperationId operationId,
    ISqlServerRuntimeHandler? runtimeHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISqlServerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopSqlServerRuntimeHandler();

    public ResourceOperationId OperationId { get; } = operationId;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == SqlServerResourceTypeProvider.ResourceTypeId &&
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
            new SqlServerLifecycleOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _runtimeHandler));
}

public sealed class SqlServerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISqlServerRuntimeHandler runtimeHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISqlServerRuntimeHandler _runtimeHandler = runtimeHandler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => Definition.Id;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable && CanExecuteForStatus(
            _runtimeHandler.GetStatus(Resource)));

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
                        "application.sqlServer.operationUnavailable",
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

    private bool CanExecuteForStatus(SqlServerRuntimeStatus status) =>
        status switch
        {
            SqlServerRuntimeStatus.Running =>
                OperationId == SqlServerResourceTypeProvider.Operations.Stop ||
                OperationId == SqlServerResourceTypeProvider.Operations.Restart,
            SqlServerRuntimeStatus.Stopped =>
                OperationId == SqlServerResourceTypeProvider.Operations.Start,
            _ => true
        };
}
