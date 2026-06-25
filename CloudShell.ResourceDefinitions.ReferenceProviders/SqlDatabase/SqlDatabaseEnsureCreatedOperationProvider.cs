namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseEnsureCreatedOperationProvider(
    ISqlDatabaseCreationHandler? creationHandler = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISqlDatabaseCreationHandler _creationHandler =
        creationHandler ?? new NoopSqlDatabaseCreationHandler();

    public ResourceOperationId OperationId =>
        SqlDatabaseResourceTypeProvider.Operations.EnsureCreated;

    public ResourceDefinitionValueSource ResolutionLevel =>
        ResourceDefinitionValueSource.TypeDefinition;

    public bool CanHandle(
        Resource resource,
        ResourceOperationResolution operation) =>
        resource.Type.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId &&
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
            new SqlDatabaseEnsureCreatedOperation(
                context.ExecutionContext ?? new ResourceProjectionExecutionContext(resource),
                operation,
                _creationHandler));
}

public sealed class SqlDatabaseEnsureCreatedOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISqlDatabaseCreationHandler creationHandler) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISqlDatabaseCreationHandler _creationHandler = creationHandler;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SqlDatabaseResourceTypeProvider.Operations.EnsureCreated;

    public bool IsAvailable => Definition.IsAvailable;

    public string? UnavailableReason => Definition.UnavailableReason;

    public ValueTask<bool> CanExecuteAsync(
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(IsAvailable);

    public ResourceDefinitionApplyStep PlanEnsureCreated() =>
        new(
            Resource.EffectiveResourceId,
            Resource.Type.TypeId,
            ResourceDefinitionApplyStepKind.MaterializeRuntime,
            $"Ensure SQL database '{Resource.Name}' exists.");

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
                        "application.sqlDatabase.ensureCreatedUnavailable",
                        UnavailableReason ?? "The SQL database ensure-created operation is not available.",
                        OperationId)
                ]);
        }

        var diagnostics = await _creationHandler.EnsureCreatedAsync(
            Resource,
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}
