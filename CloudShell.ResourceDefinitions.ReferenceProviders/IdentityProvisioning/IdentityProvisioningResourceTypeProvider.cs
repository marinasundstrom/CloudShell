namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class IdentityProvisioningResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "infrastructure";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.identity-provisioning";
    public const string ProviderId = "identity.provisioning";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId InfrastructureKind = "infrastructure.kind";
        public static readonly ResourceAttributeId IdentityProvider = "identity.provider";
        public static readonly ResourceAttributeId ProviderKind = "identity.providerKind";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId IdentityProvisioning = "identity.provisioning";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Setup = "identity.provisioning.setup";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.InfrastructureKind] = new(
                DefaultValue: "identity-provisioning",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.IdentityProvider] = new(
                Required: true,
                ValueType: ResourceAttributeValueType.String),
            [Attributes.ProviderKind] = new(
                DefaultValue: "oidc",
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(Capabilities.IdentityProvisioning)
        ],
        Operations:
        [
            new(Operations.Setup)
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
                    "Accept identity provisioning definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Set up identity provisioning resource '{resource.Name}'.")
            ],
            []));
}
