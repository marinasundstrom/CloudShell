using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class AspNetCoreProjectResourceTypeProvider(
    IAspNetCoreProjectRuntimeController? runtimeController = null) :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "project";
    public static readonly ResourceTypeId ResourceTypeId = "application.dotnet-app";
    public const string ProviderId = "applications.dotnet-app";
    public const string ConfigurationSection = "aspNetCoreProject";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ProjectPath = "project.path";
        public static readonly ResourceAttributeId ExecutablePath = "executablePath";
        public static readonly ResourceAttributeId ProjectArguments = "project.arguments";
        public static readonly ResourceAttributeId HotReload = "project.hotReload";
        public static readonly ResourceAttributeId UseLaunchSettings = "project.useLaunchSettings";
        public static readonly ResourceAttributeId EndpointRequests = "project.endpointRequests";
        public static readonly ResourceAttributeId EnvironmentVariables = "project.environmentVariables";
        public static readonly ResourceAttributeId ServiceDiscoveryName = "project.serviceDiscoveryName";
        public static readonly ResourceAttributeId References = "project.references";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
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
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ExecutablePath] = new(
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.SourceKind] = new(
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.SourceOwner] = new(
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.Enabled] = new(
                ValueType: ResourceAttributeValueType.Boolean),
            [ApplicationArtifactAttributeIds.Source] = new(
                ValueType: ResourceAttributeValueType.ComplexType),
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
            [Attributes.EnvironmentVariables] = new(
                Description: "Process environment variables keyed by variable name. Values are resolved when the resource starts.",
                ValueType: ResourceAttributeValueType.ComplexType),
            [Attributes.ServiceDiscoveryName] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.References] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ResourceReference)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.EndpointSource),
            new(ResourceCommonCapabilityIds.Monitoring),
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue)
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
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
        ValidateSource(
            resource.Attributes,
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
        ValidateSource(
            changes.ProposedState.ResourceAttributeValues,
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
                    "Accept .NET app definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize .NET app resource '{resource.Name}'.")
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
            "The .NET app graph change was saved, but the running project must be restarted before it materializes the changed runtime configuration.",
            changes.Resource.EffectiveResourceId));
    }

    private static bool RequiresRestart(ResourceChangeSet changes) =>
        changes.AttributeChanges.Any(change =>
            change.AttributeId == Attributes.ProjectPath ||
            change.AttributeId == Attributes.ExecutablePath ||
            change.AttributeId == Attributes.ProjectArguments ||
            change.AttributeId == Attributes.HotReload ||
            change.AttributeId == Attributes.UseLaunchSettings ||
            change.AttributeId == Attributes.EndpointRequests ||
            change.AttributeId == Attributes.EnvironmentVariables ||
            change.AttributeId == Attributes.ServiceDiscoveryName ||
            change.AttributeId == Attributes.References ||
            change.AttributeId == ApplicationArtifactAttributeIds.Enabled ||
            change.AttributeId == ApplicationArtifactAttributeIds.SourceKind ||
            change.AttributeId == ApplicationArtifactAttributeIds.Source);

    private static void ValidateSource(
        ResourceAttributeSet attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (ApplicationArtifactResourceValidation.UsesUploadedArtifact(attributes))
        {
            ApplicationArtifactResourceValidation.ValidateUploadedArtifact(attributes, diagnostics);
            return;
        }

        ValidateLocalSource(
            attributes.GetString(Attributes.ProjectPath),
            attributes.GetString(Attributes.ExecutablePath),
            diagnostics);
    }

    private static void ValidateSource(
        ResourceAttributeValueMap attributes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (ApplicationArtifactResourceValidation.UsesUploadedArtifact(attributes))
        {
            ApplicationArtifactResourceValidation.ValidateUploadedArtifact(attributes, diagnostics);
            return;
        }

        ValidateLocalSource(
            GetString(attributes, Attributes.ProjectPath),
            GetString(attributes, Attributes.ExecutablePath),
            diagnostics);
    }

    private static void ValidateLocalSource(
        string? projectPath,
        string? executablePath,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var hasProjectPath = !string.IsNullOrWhiteSpace(projectPath);
        var hasExecutablePath = !string.IsNullOrWhiteSpace(executablePath);
        if (!hasProjectPath && !hasExecutablePath)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.dotnetApp.sourceRequired",
                ".NET app requires either a project path or executable path.",
                Attributes.ProjectPath));
            return;
        }

        if (hasProjectPath && hasExecutablePath)
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.dotnetApp.sourceConflict",
                ".NET app local mode must use either project.path or executablePath, not both.",
                Attributes.ExecutablePath));
        }
    }

    private static string? GetString(
        ResourceAttributeValueMap attributes,
        ResourceAttributeId attributeId) =>
        attributes.TryGetValue(attributeId, out var value) &&
        value.TryGetScalarString(out var scalar)
            ? scalar
            : null;
}
