namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class NameMappingResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "network";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.nameMapping";
    public const string ProviderId = "cloudshell.dns";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId HostName = "nameMapping.hostName";
        public static readonly ResourceAttributeId TargetEndpointName = "nameMapping.targetEndpointName";
        public static readonly ResourceAttributeId Exposure = "nameMapping.exposure";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId NetworkingNameMapping = "networking.nameMapping";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.HostName] = new(
                Required: true,
                RequiredMessage: "Name-mapping host name is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.TargetEndpointName] = new(
                DefaultValue: "default",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Exposure] = new(
                DefaultValue: "Public",
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(Capabilities.NetworkingNameMapping)
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
                    "Accept name-mapping definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize name-mapping resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateRequired(
            resource.Attributes.GetString(Attributes.HostName),
            Attributes.HostName,
            diagnostics);
        ValidateHasGraphReference(resource.State.StartupDependencies, diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.HostName, out var hostName))
        {
            ValidateRequired(hostName, Attributes.HostName, diagnostics);
        }

        ValidateHasGraphReference(state.StartupDependencies, diagnostics);
        return diagnostics;
    }

    private static void ValidateRequired(
        string? value,
        ResourceAttributeId attributeId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "dns.nameMapping.hostNameRequired",
                "Name-mapping host name is required.",
                attributeId));
        }
    }

    private static void ValidateHasGraphReference(
        IReadOnlyList<ResourceReference> references,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!references.Any(reference => reference.TryGetDependsOnResourceId(out _)))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "dns.nameMapping.targetRequired",
                "Name mappings must declare at least one graph resource reference.",
                ResourceTypeId));
        }
    }
}
