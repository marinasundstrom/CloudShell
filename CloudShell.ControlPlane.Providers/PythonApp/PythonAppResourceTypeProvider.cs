namespace CloudShell.ControlPlane.Providers;

public sealed class PythonAppResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "project";
    public static readonly ResourceTypeId ResourceTypeId = "application.python-app";
    public const string ProviderId = "applications.python-app";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ProjectPath = "project.path";
        public static readonly ResourceAttributeId Command = "python.command";
        public static readonly ResourceAttributeId ScriptPath = "python.scriptPath";
        public static readonly ResourceAttributeId Module = "python.module";
        public static readonly ResourceAttributeId Arguments = "python.arguments";
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

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.ProjectPath] = new(
                Required: true,
                RequiredMessage: "Python app project path is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Command] = new(
                DefaultValue: "python3",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ScriptPath] = new(
                DefaultValue: "app.py",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Module] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Arguments] = new(
                ValueType: ResourceAttributeValueType.String),
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
            new(VolumeConsumerCapabilityProvider.CapabilityIdValue),
            new(
                ResourceLogSourceCapabilityIds.LogSources,
                ResourceDefinitionJson.FromValue(ResourceLogSourceDefinitionSet.DefaultConsole()))
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
                    "Accept Python app definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize Python app resource '{resource.Name}'.")
            ],
            []));

    private static void ValidateProjectPath(
        string? projectPath,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "application.pythonApp.pathRequired",
                "Python app project path is required.",
                Attributes.ProjectPath));
        }
    }
}
