namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlServerResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "application.sql-server";
    public const string ProviderId = "applications.sql-server";
    public const string ConfigurationSection = "sqlServer";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Version = "sqlserver.version";
        public static readonly ResourceAttributeId Edition = "sqlserver.edition";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId ReconcileAccess =
            "application.sql-server.reconcile-access";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Version] = new(
                DefaultValue: "2022",
                Required: true,
                RequiredMessage: "SQL Server version is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Edition] = new(
                DefaultValue: "Developer",
                ValueShape: new(ResourceAttributeValueKind.String))
        },
        Operations:
        [
            new(Operations.ReconcileAccess)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateVersion(resource.Attributes.GetString(Attributes.Version), diagnostics);
        ValidateDatabases(resource.GetConfiguration<SqlServerConfiguration>(ConfigurationSection), diagnostics);

        return ValueTask.FromResult(
            ResourceDefinitionValidationResult.FromDiagnostics(diagnostics));
    }

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);

        if (changes.ProposedState.ResourceAttributes.TryGetValue(Attributes.Version, out var version))
        {
            ValidateVersion(version, diagnostics);
        }

        ValidateDatabases(
            changes.ProposedState.GetConfiguration<SqlServerConfiguration>(ConfigurationSection),
            diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(changes, changes.ProposedState, diagnostics));
    }

    public bool CanPlan(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionApplyPlan> PlanApplyAsync(
        Resource resource,
        ResourceDefinitionApplyContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(new ResourceDefinitionApplyPlan(
            resource,
            [
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.AcceptDefinition,
                    "Accept SQL Server definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize SQL Server resource '{resource.Name}'.")
            ],
            []));

    private static void ValidateVersion(
        string? version,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(version))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.sqlServer.versionRequired",
                "SQL Server version is required.",
                Attributes.Version));
        }
    }

    private static void ValidateDatabases(
        SqlServerConfiguration? configuration,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var database in configuration?.Databases ?? [])
        {
            if (string.IsNullOrWhiteSpace(database.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "application.sqlServer.databaseNameRequired",
                    "SQL Server database name is required.",
                    ConfigurationSection));
            }
        }
    }
}

public sealed record SqlServerConfiguration(
    IReadOnlyList<SqlServerDatabaseDefinition> Databases);

public sealed record SqlServerDatabaseDefinition(
    string Name,
    string? DisplayName = null,
    bool EnsureCreated = false);

public sealed class SqlServerReconcileAccessOperationProvider :
    IResourceOperationProvider,
    IResourceOperationProjector
{
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
                operation));
}

public sealed class SqlServerReconcileAccessOperation(
    ResourceProjectionExecutionContext context,
    ResourceOperationResolution operation) : IResourceOperationExecutorProjection
{
    public ResourceProjectionExecutionContext Context { get; } = context;

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

        return new ResourceOperationExecutionResult(
            Resource,
            OperationId,
            []);
    }
}

public sealed record SqlServerAccessReconciliationPlan(
    Resource Resource,
    IReadOnlyList<SqlServerDatabaseDefinition> Databases);
