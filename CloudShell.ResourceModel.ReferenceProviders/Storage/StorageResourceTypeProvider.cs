namespace CloudShell.ResourceModel.ReferenceProviders;

public sealed class StorageResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "storage";
    public static readonly ResourceTypeId ResourceTypeId = "cloudshell.storage";
    public const string ProviderId = "cloudshell.storage";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId StorageKind = "storage.kind";
        public static readonly ResourceAttributeId Provider = "storage.provider";
        public static readonly ResourceAttributeId Medium = "storage.medium";
        public static readonly ResourceAttributeId Location = "storage.location";
    }

    public static class Capabilities
    {
        public static readonly ResourceCapabilityId StorageProvider = "storage.provider";
        public static readonly ResourceCapabilityId StorageMountProvider = "storage.mountProvider";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Inspect = "storage.inspect";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.StorageKind] = new(
                DefaultValue: "provider",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Provider] = new(
                DefaultValue: StorageResourceDefaults.LocalProvider,
                Required: true,
                RequiredMessage: "Storage provider is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Medium] = new(
                DefaultValue: "FileSystem",
                Required: true,
                RequiredMessage: "Storage medium is required.",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Location] = new(
                ValueType: ResourceAttributeValueType.String)
        },
        Capabilities:
        [
            new(Capabilities.StorageProvider),
            new(Capabilities.StorageMountProvider)
        ],
        Operations:
        [
            new(Operations.Inspect)
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
                    "Accept storage definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize storage resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateRequired(
            resource.Attributes.GetString(Attributes.Provider),
            Attributes.Provider,
            "storage.providerRequired",
            "Storage provider is required.",
            diagnostics);
        ValidateRequired(
            resource.Attributes.GetString(Attributes.Medium),
            Attributes.Medium,
            "storage.mediumRequired",
            "Storage medium is required.",
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.Provider, out var provider))
        {
            ValidateRequired(
                provider,
                Attributes.Provider,
                "storage.providerRequired",
                "Storage provider is required.",
                diagnostics);
        }

        if (state.ResourceAttributes.TryGetValue(Attributes.Medium, out var medium))
        {
            ValidateRequired(
                medium,
                Attributes.Medium,
                "storage.mediumRequired",
                "Storage medium is required.",
                diagnostics);
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
}
