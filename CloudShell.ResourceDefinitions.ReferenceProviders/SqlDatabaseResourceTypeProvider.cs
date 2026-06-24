namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class SqlDatabaseResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "application.sql-database";
    public const string ProviderId = "applications.sql-database";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId DatabaseName = "database.name";
        public static readonly ResourceAttributeId ServerResourceId = "database.serverResourceId";
        public static readonly ResourceAttributeId Source = "database.source";
        public static readonly ResourceAttributeId EnsureCreated = "database.ensureCreated";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId EnsureCreated =
            "application.sql-database.ensure-created";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.DatabaseName] = new(
                Required: true,
                RequiredMessage: "SQL database name is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.ServerResourceId] = new(
                Required: true,
                RequiredMessage: "SQL database server resource id is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Source] = new(
                DefaultValue: "declared",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.EnsureCreated] = new(
                DefaultValue: false,
                ValueShape: new(ResourceAttributeValueKind.Boolean))
        },
        Operations:
        [
            new(Operations.EnsureCreated)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = ValidateResolvedResource(resource);

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
        diagnostics.AddRange(ValidateExplicitState(changes.ProposedState));

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
                    "Accept SQL database definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize SQL database resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateDatabaseName(
            resource.Attributes.GetString(Attributes.DatabaseName),
            diagnostics);
        ValidateServerResourceId(
            resource.Attributes.GetString(Attributes.ServerResourceId),
            diagnostics);
        ValidateEnsureCreated(
            resource.Attributes.GetString(Attributes.EnsureCreated),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.DatabaseName, out var databaseName))
        {
            ValidateDatabaseName(databaseName, diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.ServerResourceId, out var serverResourceId))
        {
            ValidateServerResourceId(serverResourceId, diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.EnsureCreated, out var ensureCreated))
        {
            ValidateEnsureCreated(ensureCreated, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateDatabaseName(
        string? databaseName,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(databaseName))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.sqlDatabase.nameRequired",
                "SQL database name is required.",
                Attributes.DatabaseName));
        }
    }

    private static void ValidateServerResourceId(
        string? serverResourceId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(serverResourceId))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.sqlDatabase.serverResourceIdRequired",
                "SQL database server resource id is required.",
                Attributes.ServerResourceId));
        }
    }

    private static void ValidateEnsureCreated(
        string? ensureCreated,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(ensureCreated) &&
            !bool.TryParse(ensureCreated, out _))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.sqlDatabase.ensureCreatedInvalid",
                "SQL database ensure-created value must be a boolean.",
                Attributes.EnsureCreated));
        }
    }
}
