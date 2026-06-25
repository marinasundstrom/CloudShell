namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class AspNetCoreProjectResourceTypeProvider(
    IAspNetCoreProjectRuntimeController? runtimeController = null) :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "project";
    public static readonly ResourceTypeId ResourceTypeId = "application.aspnet-core-project";
    public const string ProviderId = "applications.aspnet-core-project";
    public const string ConfigurationSection = "aspNetCoreProject";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ProjectPath = "project.path";
        public static readonly ResourceAttributeId ProjectArguments = "project.arguments";
        public static readonly ResourceAttributeId HotReload = "project.hotReload";
        public static readonly ResourceAttributeId UseLaunchSettings = "project.useLaunchSettings";
        public static readonly ResourceAttributeId EndpointRequests = "project.endpointRequests";
        public static readonly ResourceAttributeId EnvironmentVariables = "project.environmentVariables";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Restart = "restart";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    private readonly IAspNetCoreProjectRuntimeController _runtimeController =
        runtimeController ?? new NoopAspNetCoreProjectRuntimeController();

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.ProjectPath] = new(
                Required: true,
                RequiredMessage: "ASP.NET Core project path is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ProjectArguments] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.HotReload] = new(
                DefaultValue: true,
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.UseLaunchSettings] = new(
                DefaultValue: true,
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest),
            [Attributes.EnvironmentVariables] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: AspNetCoreProjectShapeIds.EnvironmentVariable)
        },
        Capabilities:
        [
            new(
                ResourceLogSourceCapabilityIds.LogSources,
                ResourceDefinitionJson.FromValue(ResourceLogSourceDefinitionSet.DefaultConsole()))
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Restart)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateProjectPath(
            resource.Attributes.GetString(Attributes.ProjectPath),
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
        ValidateProjectPath(
            changes.ProposedState.ResourceAttributes.GetValueOrDefault(Attributes.ProjectPath),
            diagnostics);
        AddRestartRequiredDiagnostic(changes, diagnostics);

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
                    "Accept ASP.NET Core project definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize ASP.NET Core project resource '{resource.Name}'.")
            ],
            []));

    private void AddRestartRequiredDiagnostic(
        ResourceChangeSet changes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (changes.IsNewResource ||
            !RequiresRestart(changes) ||
            _runtimeController.GetStatus(changes.Resource) != AspNetCoreProjectRuntimeStatus.Running)
        {
            return;
        }

        diagnostics.Add(ResourceDefinitionDiagnostic.Warning(
            "application.aspNetCoreProject.restartRequired",
            "The ASP.NET Core project graph change was saved, but the running project must be restarted before it materializes the changed runtime configuration.",
            changes.Resource.EffectiveResourceId));
    }

    private static bool RequiresRestart(ResourceChangeSet changes) =>
        changes.AttributeChanges.Any(change =>
            change.AttributeId == Attributes.ProjectPath ||
            change.AttributeId == Attributes.ProjectArguments ||
            change.AttributeId == Attributes.HotReload ||
            change.AttributeId == Attributes.UseLaunchSettings ||
            change.AttributeId == Attributes.EndpointRequests ||
            change.AttributeId == Attributes.EnvironmentVariables);

    private static void ValidateProjectPath(
        string? projectPath,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.aspNetCoreProject.pathRequired",
                "ASP.NET Core project path is required.",
                Attributes.ProjectPath));
        }
    }
}
