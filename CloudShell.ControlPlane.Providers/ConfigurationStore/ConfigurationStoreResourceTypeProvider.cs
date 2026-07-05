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
        public static readonly ResourceAttributeId Settings = "seed.settings";
        public static readonly ResourceAttributeId SettingCount = "settingCount";
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
            [Attributes.Settings] = ResourceAttributeDefinition.Collection(
                ResourceAttributeValueType.ComplexType,
                itemShape: new(new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                {
                    ["name"] = new(
                        Required: true,
                        RequiredMessage: "Setting name is required.",
                        ValueType: ResourceAttributeValueType.String),
                    ["value"] = new(
                        Required: true,
                        RequiredMessage: "Setting value is required.",
                        ValueType: ResourceAttributeValueType.String)
                }),
                description: "Create-only settings used to seed a new Configuration Store resource."),
            [Attributes.SettingCount] = new(
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
                        endpointName: "settings"),
                    ResourceHealthCheckDefinition.HttpLiveness(
                        "/healthz",
                        endpointName: "settings")
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
        ValidateCreateOnlySettings(changes, diagnostics);

        return ValueTask.FromResult(diagnostics.Any(diagnostic =>
            diagnostic.Severity == ResourceDefinitionDiagnosticSeverity.Error)
                ? ResourceChangeApplyResult.Rejected(changes, diagnostics)
                : new ResourceChangeApplyResult(
                    changes,
                    StripSeedSettings(changes.ProposedState),
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
        ValidateSettingCount(
            resource.Attributes.GetString(Attributes.SettingCount),
            diagnostics);
        return diagnostics;
    }

    private static IReadOnlyList<ResourceDefinitionDiagnostic> ValidateExplicitState(
        ResourceState state)
    {
        var diagnostics = new List<ResourceDefinitionDiagnostic>();

        if (state.ResourceAttributes.TryGetValue(Attributes.SettingCount, out var settingCount))
        {
            ValidateSettingCount(settingCount, diagnostics);
        }

        if (state.ResourceAttributeValues.ContainsKey(Attributes.Settings))
        {
            ValidateSeedSettings(state, diagnostics);
        }

        return diagnostics;
    }

    private static void ValidateCreateOnlySettings(
        ResourceChangeSet changes,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!changes.IsNewResource &&
            changes.ProposedState.ResourceAttributeValues.ContainsKey(Attributes.Settings))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "configuration.store.settingsSeedUpdateNotAllowed",
                "Settings can only be supplied when creating a Configuration Store resource.",
                Attributes.Settings));
        }
    }

    private static void ValidateSeedSettings(
        ResourceState state,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        var settings = state.ResourceAttributeValues.GetObject<ConfigurationStoreSeedSetting[]>(
            Attributes.Settings) ?? [];
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var setting in settings)
        {
            if (string.IsNullOrWhiteSpace(setting.Name))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedSettingNameRequired",
                    "Configuration Store setting name is required.",
                    Attributes.Settings));
            }

            if (setting.Value is null)
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedSettingValueRequired",
                    "Configuration Store setting value is required.",
                    Attributes.Settings));
            }

            if (!string.IsNullOrWhiteSpace(setting.Name) &&
                !names.Add(setting.Name.Trim()))
            {
                diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                    "configuration.store.seedSettingDuplicate",
                    $"Configuration Store setting '{setting.Name}' is declared more than once.",
                    Attributes.Settings));
            }
        }
    }

    private static ResourceState StripSeedSettings(
        ResourceState state)
    {
        if (!state.ResourceAttributeValues.ContainsKey(Attributes.Settings))
        {
            return state;
        }

        var settings = state.ResourceAttributeValues.GetObject<ConfigurationStoreSeedSetting[]>(
            Attributes.Settings) ?? [];
        var attributes = state.ResourceAttributeValues
            .Where(attribute => attribute.Key != Attributes.Settings)
            .ToDictionary(attribute => attribute.Key, attribute => attribute.Value);
        attributes[Attributes.SettingCount] = ResourceAttributeValue.Integer(settings.Length);

        return state with
        {
            Attributes = new ResourceAttributeValueMap(attributes)
        };
    }

    private static void ValidateSettingCount(
        string? settingCount,
        List<ResourceDefinitionDiagnostic> diagnostics)
    {
        if (!string.IsNullOrWhiteSpace(settingCount) &&
            (!int.TryParse(settingCount, out var count) || count < 0))
        {
            diagnostics.Add(ResourceDefinitionDiagnostic.Error(
                "configuration.store.settingCountInvalid",
                "Configuration Store setting count must be a non-negative integer.",
                Attributes.SettingCount));
        }
    }
}
