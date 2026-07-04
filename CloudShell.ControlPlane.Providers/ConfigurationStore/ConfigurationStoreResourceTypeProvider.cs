namespace CloudShell.ControlPlane.Providers;

public sealed class ConfigurationStoreResourceTypeProvider :
    IResourceTypeProvider,
    IResourceChangeApplyProvider,
    IResourceDefinitionApplyProvider
{
    public static readonly ResourceClassId ClassId = "configuration";
    public static readonly ResourceTypeId ResourceTypeId = "configuration.store";
    public const string ProviderId = "configuration";

    public static ResourceClassDefinition ClassDefinition { get; } = new(ClassId);

    public static class Attributes
    {
        public static readonly ResourceAttributeId Kind = "kind";
        public static readonly ResourceAttributeId Endpoint = "endpoint";
        public static readonly ResourceAttributeId Entries = "seed.entries";
        public static readonly ResourceAttributeId EntryCount = "entryCount";
    }

    public static class Operations
    {
        public static readonly ResourceOperationId Start = "start";
        public static readonly ResourceOperationId Stop = "stop";
        public static readonly ResourceOperationId Restart = "restart";
        public static readonly ResourceOperationId Inspect = "configuration.store.inspect";
    }

    public ResourceTypeId TypeId => ResourceTypeId;

    public ResourceTypeDefinition TypeDefinition { get; } = new(
        ResourceTypeId,
        ClassId,
        DefaultProviderId: ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
        {
            [Attributes.Kind] = new(
                DefaultValue: "store",
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Endpoint] = new(
                ValueType: ResourceAttributeValueType.String),
            [Attributes.Entries] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Setting entry name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["value"] = new(
                        Required: true,
                        RequiredMessage: "Setting entry value is required.",
                        ValueType: ResourceAttributeValueType.String)
                }),
                description: "Create-only setting entries used to seed a new Configuration Store resource."),
            [Attributes.EntryCount] = new(
                DefaultValue: 0,
                ValueType: ResourceAttributeValueType.Integer,
                ReadOnly: true,
                Mutability: ResourceAttributeMutability.ProviderManaged)
        },
        Capabilities:
        [
            new(ResourceCommonCapabilityIds.Monitoring),
            new(ResourceCommonCapabilityIds.EndpointSource),
            new(
                ResourceHealthCheckCapabilityIds.HealthChecks,
                ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                [
                    ResourceHealthCheckDefinition.Http(
                        "/healthz",
                        endpointName: "entries"),
                    ResourceHealthCheckDefinition.HttpLiveness(
                        "/healthz",
                        endpointName: "entries")
                ])))
        ],
        Operations:
        [
            new(Operations.Start),
            new(Operations.Stop),
            new(Operations.Restart),
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
        ValidateCreateOnlyEntries(changes, diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(
                    changes,
                    StripSeedEntries(changes.ProposedState),
                    diagnostics));
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
                    "Accept configuration store definition.",
                    resource.ToDefinition()),
                new(
                    resource.EffectiveResourceId,
                    ResourceTypeId,
                    ResourceDefinitionApplyStepKind.MaterializeRuntime,
                    $"Materialize configuration store resource '{resource.Name}'.")
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

        if (state.ResourceAttributeValues.ContainsKey(Attributes.Entries))
        {
            ValidateSeedEntries(state, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateCreateOnlyEntries(
        ResourceChangeSet changes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!changes.IsNewResource &&
            changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Entries))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "configuration.store.entriesSeedUpdateNotAllowed",
                "Setting entries can only be supplied when creating a Configuration Store resource.",
                Attributes.Entries));
        }
    }

    private static void ValidateSeedEntries(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var entries = state.ResourceAttributeValues.GetObject<ConfigurationStoreSeedSetting[]>(
            Attributes.Entries) ?? [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedEntryNameRequired",
                    "Configuration Store setting entry name is required.",
                    Attributes.Entries));
            }

            if (entry.Value is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedEntryValueRequired",
                    "Configuration Store setting entry value is required.",
                    Attributes.Entries));
            }

            if (!string.IsNullOrWhiteSpace(entry.Name) &&
                !names.Add(entry.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedEntryDuplicate",
                    $"Configuration Store setting entry '{entry.Name}' is declared more than once.",
                    Attributes.Entries));
            }
        }
    }

    private static ResourceState StripSeedEntries(
        ResourceState state)
    {
        if (!state.ResourceAttributeValues.ContainsKey(Attributes.Entries))
        {
            return state;
        }

        var entries = state.ResourceAttributeValues.GetObject<ConfigurationStoreSeedSetting[]>(
            Attributes.Entries) ?? [];
        var attributes = state.ResourceAttributeValues
            .Where(attribute => attribute.Key != Attributes.Entries)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        attributes[Attributes.EntryCount] = ResourceAttributeValue.Integer(entries.Length);

        return state with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
    }

    private static void ValidateEntryCount(
        string? entryCount,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(entryCount) &&
            (!int.TryParse(entryCount, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "configuration.store.entryCountInvalid",
                "Configuration Store setting entry count must be a non-negative integer.",
                Attributes.EntryCount));
        }
    }
}
