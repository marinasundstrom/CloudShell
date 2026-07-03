namespace CloudShell.ControlPlane.Providers;

public sealed class SqlServerResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "service";
    public static readonly ResourceTypeId ResourceTypeId = "application.sql-server";
    public const string ProviderId = "applications.sql-server";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Version = "version";
        public static readonly ResourceAttributeId Edition = "edition";
        public static readonly ResourceAttributeId Databases = "databases";
        public static readonly ResourceAttributeId EndpointRequests = "endpointRequests";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
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
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Edition] = new(
                DefaultValue: "Developer",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Databases] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest)
        },
        Capabilities:
        [
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue)
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart),
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
        ValidateDatabases(
            resource.Attributes.GetObject<SqlServerDatabaseDefinition[]>(Attributes.Databases),
            diagnostics);

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
            changes.ProposedState.ResourceAttributeValues.GetObject<SqlServerDatabaseDefinition[]>(Attributes.Databases),
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
        IReadOnlyList<SqlServerDatabaseDefinition>? databases,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        foreach (var database in databases ?? [])
        {
            if (string.IsNullOrWhiteSpace(database.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "application.sqlServer.databaseNameRequired",
                    "SQL Server database name is required.",
                    Attributes.Databases));
            }
        }
    }

    internal static bool TryGetContainerHostResourceId(
        ResourceState state,
        out string containerHostResourceId)
    {
        foreach (var reference in state.StartupDependencies)
        {
            if (reference.TypeId is { } typeId &&
                IsContainerHostResourceType(typeId) &&
                reference.TryGetDependsOnResourceId(out containerHostResourceId))
            {
                return true;
            }
        }

        containerHostResourceId = string.Empty;
        return false;
    }

    internal static bool IsContainerHostResourceType(ResourceTypeId typeId) =>
        typeId == ContainerHostResourceTypeProvider.ResourceTypeId ||
        typeId == DockerHostResourceTypeProvider.ResourceTypeId;
}
