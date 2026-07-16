namespace CloudShell.ControlPlane.Providers;

public sealed class SqlDatabaseEnsureCreatedOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
    private readonly IProviderExecutionDispatcher _dispatcher;
    private readonly ISqlDatabaseCreationHandler? _creationHandler;

    public SqlDatabaseEnsureCreatedOperationProvider(
        ISqlDatabaseCreationHandler? creationHandler = null,
        IProviderExecutionDispatcher? dispatcher = null)
    {
        _creationHandler = creationHandler;
        _dispatcher = dispatcher ?? new InProcessProviderExecutionDispatcher(
            [
                new SqlDatabaseEnsureCreatedExecutionHandler(creationHandler)
            ]);
    }

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
                _dispatcher,
                GetUnavailableReason(resource)));

    private string? GetUnavailableReason(Resource resource) =>
        SqlDatabaseCreationReadiness.IsMissing(_creationHandler)
            ? SqlDatabaseCreationReadiness.CreateMissingReason(resource)
            : null;
}

public sealed class SqlDatabaseEnsureCreatedOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation,
    IProviderExecutionDispatcher dispatcher,
    string? unavailableReason = null) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

    private readonly IProviderExecutionDispatcher _dispatcher = dispatcher;

    public Resource Resource => Context.Resource;

    public ResourceOperationResolution Definition { get; } = operation;

    public ResourceOperationId OperationId => SqlDatabaseResourceTypeProvider.Operations.EnsureCreated;

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

        var result = await _dispatcher.ExecuteAsync(
            ProviderExecutionRequests.CreateForResource(
                Resource,
                OperationId.Value,
                ProviderExecutionInstructionTypes.SqlDatabaseEnsureCreated,
                [ProviderExecutionCapabilities.SqlDatabaseCreation],
                Context.Resources),
            cancellationToken);

        return result.ToResourceOperationExecutionResult(Resource, OperationId);
    }
}
