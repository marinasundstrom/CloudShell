namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class VirtualNetworkResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "network";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.virtualNetwork";
    public const string ProviderId = "cloudshell.network";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId NetworkKind = "network.kind";
        public static readonly ResourceAttributeId IsDefault = "network.default";
        public static readonly ResourceAttributeId HostReadiness = "network.hostReadiness";
        public static readonly ResourceAttributeId MappingProviders = "network.mappingProviders";
        public static readonly ResourceAttributeId Endpoints = "network.endpoints";
        public static readonly ResourceAttributeId EndpointNetworkMappings = "network.endpointNetworkMappings";
        public static readonly ResourceAttributeId EndpointMappings = "network.endpointMappings";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId NetworkingProvider = "networking.provider";
        public static readonly ResourceCapabilityId NetworkingEndpointProvider = "networking.endpointProvider";
        public static readonly ResourceCapabilityId NetworkingEndpointMapper = "networking.endpointMapper";
        public static readonly ResourceCapabilityId NetworkingVirtualNetwork = "networking.virtualNetwork";
        public static readonly ResourceCapabilityId NetworkingIngress = "networking.ingress";
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
                DefaultValue: "Virtual",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.IsDefault] = new(
                DefaultValue: false,
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.HostReadiness] = new(
                DefaultValue: "logicalOnly",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.MappingProviders] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Endpoints] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.Endpoint),
            [Attributes.EndpointNetworkMappings] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointNetworkMapping),
            [Attributes.EndpointMappings] = ResourceAttributeDefinition.Collection(
                itemType: ResourceAttributeValueType.ComplexType,
                itemShapeId: NetworkingEndpointShapeIds.EndpointMapping)
        },
        Capabilities:
        [
            new(Capabilities.NetworkingProvider),
            new(Capabilities.NetworkingEndpointProvider),
            new(Capabilities.NetworkingEndpointMapper),
            new(Capabilities.NetworkingVirtualNetwork),
            new(Capabilities.NetworkingIngress)
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
        ValueTask.FromResult(ResourceDefinitionValidationResult.FromDiagnostics(
            ValidateResolvedResource(resource)));

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
                    "Accept virtual network definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize virtual network resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateBoolean(
            resource.Attributes.GetString(Attributes.IsDefault),
            Attributes.IsDefault,
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.IsDefault, out var isDefault))
        {
            ValidateBoolean(isDefault, Attributes.IsDefault, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateBoolean(
        string? value,
        ResourceAttributeId attributeId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !bool.TryParse(value, out _))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "network.virtual.defaultInvalid",
                "Virtual network default marker must be a boolean value.",
                attributeId));
        }
    }
}
