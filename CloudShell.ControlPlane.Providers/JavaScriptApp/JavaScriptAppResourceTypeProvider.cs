using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class JavaScriptAppResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "project";
    public static readonly ResourceTypeId ResourceTypeId = "application.javascript-app";
    public const string ProviderId = "applications.javascript-app";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ProjectPath = "javascript-app:project.path";
        public static readonly ResourceAttributeId Runtime = "javascript-app:runtime";
        public static readonly ResourceAttributeId PackageManager = "javascript-app:packageManager";
        public static readonly ResourceAttributeId Script = "javascript-app:script";
        public static readonly ResourceAttributeId Arguments = "javascript-app:arguments";
        public static readonly ResourceAttributeId EndpointRequests = "javascript-app:project.endpointRequests";
        public static readonly ResourceAttributeId EnvironmentVariables = "javascript-app:project.environmentVariables";
        public static readonly ResourceAttributeId ServiceDiscoveryName = "javascript-app:project.serviceDiscoveryName";
        public static readonly ResourceAttributeId References = "javascript-app:project.references";
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
                Path: "project.path",
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.SourceKind] = new(
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.SourceOwner] = new(
                ValueType: ResourceAttributeValueType.String),
            [ApplicationArtifactAttributeIds.Enabled] = new(
                ValueType: ResourceAttributeValueType.Boolean),
            [ApplicationArtifactAttributeIds.Source] = new(
                ValueType: ResourceAttributeValueType.ComplexType),
            [Attributes.Runtime] = new(
                DefaultValue: "node",
                Path: "runtime",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.PackageManager] = new(
                DefaultValue: "npm",
                Path: "packageManager",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Script] = new(
                DefaultValue: "dev",
                Path: "script",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Arguments] = new(
                Path: "arguments",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.EndpointRequests] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointRequest,
                path: "project.endpointRequests"),
            [Attributes.EnvironmentVariables] = new(
                Description: "Process environment variables keyed by variable name. Values are resolved when the resource starts.",
                Path: "project.environmentVariables",
                ValueType: ResourceAttributeValueType.ComplexType),
            [Attributes.ServiceDiscoveryName] = new(
                Path: "project.serviceDiscoveryName",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.References] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ResourceReference,
                path: "project.references")
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
                    "Accept JavaScript app definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize JavaScript app resource '{resource.Name}'.")
            ],
            []));

    private static void ValidateSource(
        ResourceAttributeSet attributes,
        List<ResourceDefinitionDiagnostic> diagnostics) =>
        ApplicationArtifactResourceValidation.ValidateSource(
            attributes,
            Attributes.ProjectPath,
            "application.javaScriptApp.pathRequired",
            "JavaScript app project path is required.",
            diagnostics);

    private static void ValidateSource(
        ResourceAttributeValueMap attributes,
        List<ResourceDefinitionDiagnostic> diagnostics) =>
        ApplicationArtifactResourceValidation.ValidateSource(
            attributes,
            Attributes.ProjectPath,
            "application.javascriptApp.pathRequired",
            "JavaScript app project path is required.",
            diagnostics);
}
