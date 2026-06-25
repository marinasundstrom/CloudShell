namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NetworkResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "network";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.network";
    public const string ProviderId = "cloudshell.network";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId NetworkKind = "network.kind";
        public static readonly ResourceAttributeId HostReadiness = "network.hostReadiness";
        public static readonly ResourceAttributeId MappingProviders = "network.mappingProviders";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId NetworkingProvider = "networking.provider";
        public static readonly ResourceCapabilityId NetworkingEndpointProvider = "networking.endpointProvider";
        public static readonly ResourceCapabilityId NetworkingEndpointMapper = "networking.endpointMapper";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId ReconcileEndpointMappings = "reconcileEndpointMappings";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.NetworkKind] = new(
                DefaultValue: "Logical",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.HostReadiness] = new(
                DefaultValue: "logicalOnly",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.MappingProviders] = new(
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(Capabilities.NetworkingProvider),
            new(Capabilities.NetworkingEndpointProvider),
            new(Capabilities.NetworkingEndpointMapper)
        ],
        Operations:
        [
            new(Operations.ReconcileEndpointMappings)
        ]);

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(ResourceDefinitionValidationResult.Success);

    public bool CanApply(ResourceChangeSet changes) =>
        changes.Resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceChangeApplyResult> ApplyChangesAsync(
        ResourceChangeSet changes,
        ResourceChangeApplyContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>(changes.Diagnostics);

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
                    "Accept network definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize network resource '{resource.Name}'.")
            ],
            []));
}
