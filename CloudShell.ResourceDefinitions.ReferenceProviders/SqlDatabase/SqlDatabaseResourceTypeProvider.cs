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
        public static readonly ResourceAttributeId Server = "database.server";
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
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Server] = new(
                Description: "SQL Server resource reference.",
                ValueType: ResourceAttributeValueType.ComplexType,
                ValueShapeId: ResourceReference.AttributeValueShapeId),
            [Attributes.Source] = new(
                DefaultValue: "declared",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.EnsureCreated] = new(
                DefaultValue: false,
                ValueType: ResourceAttributeValueType.Boolean)
        },
        AttributeValueShapes: new Dictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>
        {
            [ResourceReference.AttributeValueShapeId] =
                ResourceReference.CreateAttributeValueShapeDefinition(
                    "SQL database resource reference.")
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
        ValidateServerReference(resource.State, diagnostics);

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
        ValidateServerReference(changes.ProposedState, diagnostics);

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

    private static List<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateDatabaseName(
            resource.Attributes.GetString(Attributes.DatabaseName),
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

    private static void ValidateServerReference(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (TryGetServerResourceId(state, out _))
        {
            return;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Error(
            "application.sqlDatabase.serverReferenceRequired",
            "SQL database server reference is required.",
            "dependsOn"));
    }

    internal static bool TryGetServerResourceId(
        ResourceState state,
        out string serverResourceId)
    {
        foreach (var reference in state.ResourceDependencies)
        {
            if (reference.TryGetResourceId(out serverResourceId))
            {
                return true;
            }
        }

        serverResourceId = string.Empty;
        return false;
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
