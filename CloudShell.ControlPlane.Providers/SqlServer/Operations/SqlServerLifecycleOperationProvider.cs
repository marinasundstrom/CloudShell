namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerStartOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerStartOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Start,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class SqlServerStopOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerStopOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Stop,
            runtimeHandler,
            dispatcher)
    {
    }
}

public sealed class SqlServerRestartOperationProvider :
    SqlServerLifecycleOperationProvider
{
    public SqlServerRestartOperationProvider(
        ISqlServerRuntimeHandler? runtimeHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
        : base(
            SqlServerResourceTypeProvider.Operations.Restart,
            runtimeHandler,
            dispatcher)
    {
    }
}

public abstract class SqlServerLifecycleOperationProvider(
    ResourceOperationId operationId,
    ISqlServerRuntimeHandler? runtimeHandler = null,
    IProviderExecutionDispatcher? dispatcher = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISqlServerRuntimeHandler _runtimeHandler =
        runtimeHandler ?? new NoopSqlServerRuntimeHandler();

    private readonly IProviderExecutionDispatcher _dispatcher =
        dispatcher ?? CreateDefaultDispatcher(
            runtimeHandler ?? new NoopSqlServerRuntimeHandler());

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
                _runtimeHandler,
                _dispatcher));

    private static IProviderExecutionDispatcher CreateDefaultDispatcher(
        ISqlServerRuntimeHandler runtimeHandler) =>
        new InProcessProviderExecutionDispatcher(
            [
                new SqlServerStartExecutionHandler(runtimeHandler),
                new SqlServerStopExecutionHandler(runtimeHandler),
                new SqlServerRestartExecutionHandler(runtimeHandler)
            ]);
}

public sealed class SqlServerLifecycleOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISqlServerRuntimeHandler runtimeHandler,
    IProviderExecutionDispatcher dispatcher) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISqlServerRuntimeHandler _runtimeHandler = runtimeHandler;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

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

        var result = await _dispatcher.ExecuteAsync(
            new ProviderExecutionRequest
            {
                AssignmentId = $"{Resource.EffectiveResourceId}:{OperationId}",
                InstructionType = GetInstructionType(OperationId),
                TargetResourceId = Resource.EffectiveResourceId,
                DesiredGeneration = Resource.Revision.Value,
                IdempotencyKey = $"{Resource.EffectiveResourceId}:{OperationId}:{Resource.Revision.Value}",
                RequiredCapabilities = [ProviderExecutionCapabilities.Containers],
                TargetResourceSnapshot = Resource,
                ResourceSnapshot = Context.Resources,
                RequestedAt = DateTimeOffset.UtcNow
            },
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            result.Diagnostics);
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

    private static string GetInstructionType(ResourceOperationId operationId)
    {
        if (operationId == SqlServerResourceTypeProvider.Operations.Start)
        {
            return ProviderExecutionInstructionTypes.SqlServerStart;
        }

        if (operationId == SqlServerResourceTypeProvider.Operations.Stop)
        {
            return ProviderExecutionInstructionTypes.SqlServerStop;
        }

        if (operationId == SqlServerResourceTypeProvider.Operations.Restart)
        {
            return ProviderExecutionInstructionTypes.SqlServerRestart;
        }

        return operationId.Value;
    }
}
