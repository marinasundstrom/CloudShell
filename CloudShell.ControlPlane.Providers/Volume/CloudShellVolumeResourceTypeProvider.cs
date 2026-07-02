namespace CloudShell.ControlPlane.Providers;

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
        public static readonly ResourceAttributeId MaxSizeBytes = "storage.volume.maxSizeBytes";
        public static readonly ResourceAttributeId MaxSizeEnforcement = "storage.volume.maxSizeEnforcement";
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
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Provider] = new(
                DefaultValue: StorageResourceDefaults.LocalProvider,
                ValueType: ResourceAttributeValueType.String),
            [Attributes.StorageMedium] = new(
                DefaultValue: StorageResourceDefaults.FileSystemMedium,
                Required: true,
                RequiredMessage: "Volume storage medium is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Location] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.SubPath] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.AccessMode] = new(
                DefaultValue: StorageResourceDefaults.ReadWriteOnceAccessMode,
                Required: true,
                RequiredMessage: "Volume access mode is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Persistent] = new(
                DefaultValue: true,
                ValueType: ResourceAttributeValueType.Boolean),
            [Attributes.MaxSizeBytes] = new(
                ValueType: ResourceAttributeValueType.Integer),
            [Attributes.MaxSizeEnforcement] = new(
                ValueType: ResourceAttributeValueType.String)
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
        ValidateAccessMode(
            resource.Attributes.GetString(Attributes.AccessMode),
            Attributes.AccessMode,
            diagnostics);
        ValidateBoolean(
            resource.Attributes.GetString(Attributes.Persistent),
            Attributes.Persistent,
            diagnostics);
        ValidatePositiveLong(
            resource.Attributes.GetString(Attributes.MaxSizeBytes),
            Attributes.MaxSizeBytes,
            "storage.volume.maxSizeBytesInvalid",
            "Volume max size must be a positive byte count.",
            diagnostics);
        ValidateMaxSizeEnforcement(
            resource.Attributes.GetString(Attributes.MaxSizeEnforcement),
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
            ValidateAccessMode(
                accessMode,
                Attributes.AccessMode,
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.Persistent, out var persistent))
        {
            ValidateBoolean(persistent, Attributes.Persistent, diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.MaxSizeBytes, out var maxSizeBytes))
        {
            ValidatePositiveLong(
                maxSizeBytes,
                Attributes.MaxSizeBytes,
                "storage.volume.maxSizeBytesInvalid",
                "Volume max size must be a positive byte count.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.MaxSizeEnforcement, out var maxSizeEnforcement))
        {
            ValidateMaxSizeEnforcement(maxSizeEnforcement, diagnostics);
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

    private static void ValidateAccessMode(
        string? value,
        ResourceAttributeId attributeId,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !Enum.TryParse<StorageVolumeAccessMode>(value, ignoreCase: true, out _))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "storage.volume.accessModeInvalid",
                "Volume access mode must be ReadWriteOnce, ReadOnlyMany, or ReadWriteMany.",
                attributeId));
        }
    }

    private static void ValidatePositiveLong(
        string? value,
        ResourceAttributeId attributeId,
        string code,
        string message,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            (!long.TryParse(value, out var maxSizeBytes) || maxSizeBytes <= 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                code,
                message,
                attributeId));
        }
    }

    private static void ValidateMaxSizeEnforcement(
        string? value,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(value) &&
            !string.Equals(value, CloudShell.Abstractions.ResourceManager.VolumeMaxSizeEnforcementModes.Advisory, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, CloudShell.Abstractions.ResourceManager.VolumeMaxSizeEnforcementModes.Enforced, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(value, CloudShell.Abstractions.ResourceManager.VolumeMaxSizeEnforcementModes.Unknown, StringComparison.OrdinalIgnoreCase))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "storage.volume.maxSizeEnforcementInvalid",
                "Volume max-size enforcement must be advisory, enforced, or unknown.",
                Attributes.MaxSizeEnforcement));
        }
    }
}
