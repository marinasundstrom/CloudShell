namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class LocalVolumeResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "storage";
    public static readonly ResourceTypeId ResourceTypeId = "storage.volume";
    public const string ProviderId = "storage.localVolume";

    public static ResourceClassDefinition ClassDefinition { get; } = new(
        ClassId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.StorageKind] = new(DefaultValue: "volume")
        });

    public static class Attributes
    {
        public static readonly ResourceAttributeId StorageKind = "storage.kind";
        public static readonly ResourceAttributeId StorageMedium = "storage.medium";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.StorageMedium] = new(
                DefaultValue: "local",
                Required: true,
                RequiredMessage: "Storage medium is required.",
                ValueShape: new(ResourceAttributeValueKind.String))
        });

    public bool CanValidate(Resource resource) =>
        resource.Type.TypeId == ResourceTypeId;

    public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
        Resource resource,
        ResourceProviderContext context,
        CancellationToken cancellationToken = default)
    {
        var diagnostics = ValidateResolvedResource(resource);

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
                    "Accept local volume definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize local volume resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var medium = resource.Attributes.GetString(Attributes.StorageMedium);

        return string.IsNullOrWhiteSpace(medium)
            ?
            [
                ResourceDefinitionDiagnostic.Error(
                    "storage.volume.mediumRequired",
                    "Storage medium is required.",
                    Attributes.StorageMedium)
            ]
            : [];
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        if (state.ResourceAttributes.TryGetValue(Attributes.StorageMedium, out var medium) &&
            string.IsNullOrWhiteSpace(medium))
        {
            return
            [
                ResourceDefinitionDiagnostic.Error(
                    "storage.volume.mediumRequired",
                    "Storage medium is required.",
                    Attributes.StorageMedium)
            ];
        }

        return [];
    }
}
