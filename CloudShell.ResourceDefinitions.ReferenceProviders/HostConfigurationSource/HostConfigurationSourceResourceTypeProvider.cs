namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public sealed class HostConfigurationSourceResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "configuration";
    public static readonly ResourceTypeId ResourceTypeId = "configuration.host";
    public const string ProviderId = "host-configuration";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId ConfigurationKind = "configuration.kind";
        public static readonly ResourceAttributeId Source = "configuration.source";
        public static readonly ResourceAttributeId EntryCount = "configuration.entries";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Inspect = "configuration.host.inspect";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.ConfigurationKind] = new(
                DefaultValue: "host",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Source] = new(
                DefaultValue: "host",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.EntryCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged)
        },
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
                    "Accept host configuration source definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize host configuration source resource '{resource.Name}'.")
            ],
            []));

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateResolvedResource(
        Resource resource)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();
        ValidateEntryCount(
            resource.Attributes.GetString(Attributes.EntryCount),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.EntryCount, out var entryCount))
        {
            ValidateEntryCount(entryCount, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateEntryCount(
        string? entryCount,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(entryCount) &&
            (!int.TryParse(entryCount, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "configuration.host.entryCountInvalid",
                "Host configuration entry count must be a non-negative integer.",
                Attributes.EntryCount));
        }
    }
}
