namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class MacOSHostNetworkResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "infrastructure";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.hostNetworking.macos";
    public const string ProviderId = "cloudshell.hostNetworking";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId InfrastructureKind = "infrastructure.kind";
        public static readonly ResourceAttributeId HostReadiness = "network.hostReadiness";
        public static readonly ResourceAttributeId HostOperatingSystem = "host.os";
        public static readonly ResourceAttributeId NetworkingMode = "networking.mode";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId NetworkingProvider = "networking.provider";
        public static readonly ResourceCapabilityId NetworkingEndpointMapper = "networking.endpointMapper";
        public static readonly ResourceCapabilityId NetworkingGateway = "networking.gateway";
        public static readonly ResourceCapabilityId NetworkingIngress = "networking.ingress";
        public static readonly ResourceCapabilityId NetworkingHostNetwork = "networking.hostNetwork";
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
            [Attributes.InfrastructureKind] = new(
                DefaultValue: "hostNetworking",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.HostReadiness] = new(
                DefaultValue: "ready",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.HostOperatingSystem] = new(
                DefaultValue: "macos",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.NetworkingMode] = new(
                DefaultValue: "localProxy",
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(Capabilities.NetworkingProvider),
            new(Capabilities.NetworkingEndpointMapper),
            new(Capabilities.NetworkingGateway),
            new(Capabilities.NetworkingIngress),
            new(Capabilities.NetworkingHostNetwork)
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
                    "Accept macOS host networking definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize macOS host networking resource '{resource.Name}'.")
            ],
            []));
}
