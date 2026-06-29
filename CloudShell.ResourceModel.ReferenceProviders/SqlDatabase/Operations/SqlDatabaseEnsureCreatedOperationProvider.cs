namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class SqlDatabaseEnsureCreatedOperationProvider(
    ISqlDatabaseCreationHandler? creationHandler = null,
    ISqlDatabaseServerResolver? serverResolver = null) :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly ISqlDatabaseCreationHandler _creationHandler =
        creationHandler ?? new NoopSqlDatabaseCreationHandler();
    private readonly ISqlDatabaseServerResolver _serverResolver =
        serverResolver ?? new ContextSqlDatabaseServerResolver();

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
                _creationHandler,
                _serverResolver));
}

public sealed class SqlDatabaseEnsureCreatedOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    ISqlDatabaseCreationHandler creationHandler,
    ISqlDatabaseServerResolver serverResolver) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly ISqlDatabaseCreationHandler _creationHandler = creationHandler;
    private readonly ISqlDatabaseServerResolver _serverResolver = serverResolver;

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

        if (!SqlDatabaseResourceTypeProvider.TryGetServerDependencyResourceId(
                Resource.State,
                out var serverResourceId))
        {
            return new ResourceOperationExecutionResult(
                Resource,
                OperationId,
                [
                    ResourceDefinitionDiagnostic.Error(
                        "application.sqlDatabase.serverReferenceRequired",
                        "SQL database owning server reference is required.",
                        Resource.EffectiveResourceId)
                ]);
        }

        var server = await _serverResolver.ResolveServerAsync(
            Resource,
            Context,
            cancellationToken);
        if (server is null)
        {
            return new ResourceOperationExecutionResult(
                Resource,
                OperationId,
                [
                    ResourceDefinitionDiagnostic.Error(
                        ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing,
                        $"SQL database owning server resource '{serverResourceId}' was not resolved.",
                        serverResourceId)
                ]);
        }

        var diagnostics = await _creationHandler.EnsureCreatedAsync(
            new SqlDatabaseCreationContext(Resource, server),
            cancellationToken);

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            diagnostics);
    }
}
