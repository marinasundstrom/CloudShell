namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerReconcileAccessOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly ISqlServerAccessReconciler? _accessReconciler;

    public SqlServerReconcileAccessOperationProvider(
        IProviderExecutionDispatcher? dispatcher = null,
        ISqlServerAccessReconciler? accessReconciler = null)
    {
        _accessReconciler = accessReconciler;
        _dispatcher = dispatcher ??
            new InProcessProviderExecutionDispatcher(
                [new SqlServerAccessReconcileExecutionHandler(accessReconciler)]);
    }

    public ResourceOperationId OperationId =>
        SqlServerResourceTypeProvider.Operations.ReconcileAccess;

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
            new SqlServerReconcileAccessOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        SqlServerAccessReconcilerReadiness.IsMissing(_accessReconciler)
            ? SqlServerAccessReconcilerReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class SqlServerReconcileAccessOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SqlServerResourceTypeProvider.Operations.ReconcileAccess;

    public bool IsAvailable =>
        Definition.IsAvailable &&
        string.IsNullOrWhiteSpace(UnavailableReason);

    public string? UnavailableReason { get; } =
        string.IsNullOrWhiteSpace(unavailableReason)
            ? operation.UnavailableReason
            : unavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public SqlServerAccessReconciliationPlan PlanReconciliation()
    {
        var databases = Resource.Attributes.GetObject<SqlServerDatabaseDefinition[]>(
            SqlServerResourceTypeProvider.Attributes.Databases);

        return new SqlServerAccessReconciliationPlan(
            Resource,
            databases ?? []);
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
                        "application.sqlServer.reconcileAccessUnavailable",
                        UnavailableReason ?? "The SQL Server access reconciliation operation is not available.",
                        OperationId)
                ]);
        }

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.SqlServerAccessReconcile,
                [ProviderExecutionCapabilities.SqlServerAccess],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}

public sealed record SqlServerAccessReconciliationPlan(
    Resource Resource,
    IReadOnlyList<SqlServerDatabaseDefinition> Databases);
