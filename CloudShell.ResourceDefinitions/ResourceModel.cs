using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceState(
    string Name,
    ResourceTypeId TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyDictionary<ResourceAttributeId, string>? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<ResourceOperationId, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly IReadOnlyList<string> EmptyList = [];
    private static readonly IReadOnlyDictionary<ResourceAttributeId, string> EmptyAttributes =
        new Dictionary<ResourceAttributeId, string>();
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyConfigurationPayloads =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<ResourceCapabilityId, JsonElement> EmptyCapabilityPayloads =
        new Dictionary<ResourceCapabilityId, JsonElement>();
    private static readonly IReadOnlyDictionary<ResourceOperationId, JsonElement> EmptyOperationPayloads =
        new Dictionary<ResourceOperationId, JsonElement>();

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    public IReadOnlyList<string> ResourceDependencies => DependsOn ?? EmptyList;

    public IReadOnlyDictionary<ResourceAttributeId, string> ResourceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyDictionary<string, JsonElement> ConfigurationPayloads => Configuration ?? EmptyConfigurationPayloads;

    public IReadOnlyDictionary<ResourceCapabilityId, JsonElement> CapabilityPayloads =>
        Capabilities ?? EmptyCapabilityPayloads;

    public IReadOnlyDictionary<ResourceOperationId, JsonElement> OperationPayloads =>
        Operations ?? EmptyOperationPayloads;

    public static ResourceState FromDefinition(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new(
            definition.Name,
            definition.TypeId,
            definition.ResourceId,
            definition.ProviderId,
            definition.DisplayName,
            definition.Version,
            definition.DependsOn,
            definition.Attributes,
            definition.Configuration,
            definition.Capabilities,
            definition.Operations,
            definition.Metadata);
    }

    public ResourceState ApplyDefinition(ResourceDefinition definition) =>
        FromDefinition(definition);

    public ResourceDefinition ToDefinition() =>
        new(
            Name,
            TypeId,
            ResourceId,
            ProviderId,
            DisplayName,
            Version,
            ResourceDependencies,
            ResourceAttributes,
            ConfigurationPayloads,
            CapabilityPayloads,
            OperationPayloads,
            Metadata);

    public TConfiguration? GetConfiguration<TConfiguration>(
        string sectionName,
        JsonSerializerOptions? options = null) =>
        ConfigurationPayloads.TryGetValue(sectionName, out var payload)
            ? payload.Deserialize<TConfiguration>(options)
            : default;

    public TCapability? GetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        CapabilityPayloads.TryGetValue(capabilityId, out var payload)
            ? payload.Deserialize<TCapability>(options)
            : default;

    public TOperation? GetOperation<TOperation>(
        ResourceOperationId operationId,
        JsonSerializerOptions? options = null) =>
        OperationPayloads.TryGetValue(operationId, out var payload)
            ? payload.Deserialize<TOperation>(options)
            : default;
}

public sealed record ResourceClass(
    ResourceClassDefinition Definition,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations)
{
    public ResourceClassId ClassId => Definition.ClassId;
}

public sealed record ResourceType(
    ResourceTypeDefinition Definition,
    ResourceClass Class,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations)
{
    public ResourceTypeId TypeId => Definition.TypeId;

    public ResourceClassId ClassId => Definition.ClassId;
}

public sealed record Resource(
    ResourceState State,
    ResourceClass Class,
    ResourceType Type,
    ResourceAttributeSet Attributes,
    ResourceCapabilitySet Capabilities,
    ResourceOperationSet Operations,
    IReadOnlyList<ResourceDefinitionDiagnostic> Diagnostics)
{
    public string Name => State.Name;

    public string EffectiveResourceId => State.EffectiveResourceId;

    public ResourceDefinition ToDefinition() => State.ToDefinition();

    public TConfiguration? GetConfiguration<TConfiguration>(
        string sectionName,
        JsonSerializerOptions? options = null) =>
        State.GetConfiguration<TConfiguration>(sectionName, options);

    public TCapability? GetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        State.GetCapability<TCapability>(capabilityId, options);

    public TOperation? GetOperation<TOperation>(
        ResourceOperationId operationId,
        JsonSerializerOptions? options = null) =>
        State.GetOperation<TOperation>(operationId, options);
}
