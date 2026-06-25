namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlServerReconcileAccessOperationProvider(
    ISqlServerAccessReconciler? accessReconciler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISqlServerAccessReconciler _accessReconciler =
        accessReconciler ?? new NoopSqlServerAccessReconciler();

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
                _accessReconciler));
}

public sealed class SqlServerReconcileAccessOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISqlServerAccessReconciler accessReconciler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISqlServerAccessReconciler _accessReconciler = accessReconciler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SqlServerResourceTypeProvider.Operations.ReconcileAccess;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public SqlServerAccessReconciliationPlan PlanReconciliation()
    {
        var configuration = Resource.GetConfiguration<SqlServerConfiguration>(
            SqlServerResourceTypeProvider.ConfigurationSection);

        return new SqlServerAccessReconciliationPlan(
            Resource,
            configuration?.Databases ?? []);
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

        var diagnostics = await _accessReconciler.ReconcileAccessAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}

public sealed record SqlServerAccessReconciliationPlan(
    Resource Resource,
    IReadOnlyList<SqlServerDatabaseDefinition> Databases);
