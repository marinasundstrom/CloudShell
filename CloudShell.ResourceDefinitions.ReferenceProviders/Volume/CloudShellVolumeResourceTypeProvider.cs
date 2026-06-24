namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class CloudShellVolumeResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "storage";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.volume";
    public const string ProviderId = "cloudshell.storage";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId StorageKind = "storage.kind";
        public static readonly ResourceAttributeId Provider = "storage.volume.provider";
        public static readonly ResourceAttributeId StorageMedium = "storage.volume.medium";
        public static readonly ResourceAttributeId Location = "storage.volume.location";
        public static readonly ResourceAttributeId SubPath = "storage.volume.subPath";
        public static readonly ResourceAttributeId AccessMode = "storage.volume.accessMode";
        public static readonly ResourceAttributeId Persistent = "storage.volume.persistent";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId StorageVolume = "storage.volume";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Provision = "storage.volume.provision";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.StorageKind] = new(
                DefaultValue: "volume",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Provider] = new(
                DefaultValue: "Local Storage",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.StorageMedium] = new(
                DefaultValue: "FileSystem",
                Required: true,
                RequiredMessage: "Volume storage medium is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Location] = new(
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.SubPath] = new(
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.AccessMode] = new(
                DefaultValue: "ReadWriteOnce",
                Required: true,
                RequiredMessage: "Volume access mode is required.",
                ValueShape: new(ResourceAttributeValueKind.String)),
            [Attributes.Persistent] = new(
                DefaultValue: true,
                ValueShape: new(ResourceAttributeValueKind.Boolean))
        },
        Capabilities:
        [
            new(Capabilities.StorageVolume)
        ],
        Operations:
        [
            new(Operations.Provision)
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
                    "Accept volume definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize volume resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateRequired(
            resource.Attributes.GetString(Attributes.StorageMedium),
            Attributes.StorageMedium,
            "storage.volume.mediumRequired",
            "Volume storage medium is required.",
            diagnostics);
        ValidateRequired(
            resource.Attributes.GetString(Attributes.AccessMode),
            Attributes.AccessMode,
            "storage.volume.accessModeRequired",
            "Volume access mode is required.",
            diagnostics);
        ValidateBoolean(
            resource.Attributes.GetString(Attributes.Persistent),
            Attributes.Persistent,
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.StorageMedium, out var medium))
        {
            ValidateRequired(
                medium,
                Attributes.StorageMedium,
                "storage.volume.mediumRequired",
                "Volume storage medium is required.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.AccessMode, out var accessMode))
        {
            ValidateRequired(
                accessMode,
                Attributes.AccessMode,
                "storage.volume.accessModeRequired",
                "Volume access mode is required.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.Persistent, out var persistent))
        {
            ValidateBoolean(persistent, Attributes.Persistent, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateRequired(
        string? value,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                code,
                message,
                attributeId));
        }
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
                "storage.volume.persistentInvalid",
                "Volume persistence must be a boolean value.",
                attributeId));
        }
    }
}
