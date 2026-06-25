using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceDefinition(
    string Name,
    ResourceTypeId TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<ResourceReference>? DependsOn = null,
    ResourceAttributeValueMap? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<ResourceOperationId, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly IReadOnlyList<ResourceReference> EmptyReferences = [];
    private static readonly ResourceAttributeValueMap EmptyAttributeValues =
        new(new Dictionary<ResourceAttributeId, ResourceAttributeValue>());
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyConfigurationPayloads =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<ResourceCapabilityId, JsonElement> EmptyCapabilityPayloads =
        new Dictionary<ResourceCapabilityId, JsonElement>();
    private static readonly IReadOnlyDictionary<ResourceOperationId, JsonElement> EmptyOperationPayloads =
        new Dictionary<ResourceOperationId, JsonElement>();

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    public IReadOnlyList<ResourceReference> StartupDependencies => DependsOn ?? EmptyReferences;

    public IReadOnlyList<string> StartupDependencyIds => StartupDependencies
        .Where(dependency => dependency.TryGetDependsOnResourceId(out _))
        .Select(dependency =>
        {
            dependency.TryGetDependsOnResourceId(out var resourceId);
            return resourceId;
        })
        .ToArray();

    public IReadOnlyList<ResourceReference> ResourceDependencies => StartupDependencies;

    public IReadOnlyList<string> ResourceDependencyIds => StartupDependencyIds;

    public ResourceAttributeValueMap ResourceAttributeValues =>
        Attributes ?? EmptyAttributeValues;

    public IReadOnlyDictionary<ResourceAttributeId, string> ResourceAttributes =>
        ResourceAttributeValueMaps.ToScalars(Attributes);

    public IReadOnlyDictionary<string, JsonElement> ConfigurationPayloads => Configuration ?? EmptyConfigurationPayloads;

    public IReadOnlyDictionary<ResourceCapabilityId, JsonElement> CapabilityPayloads =>
        Capabilities ?? EmptyCapabilityPayloads;

    public IReadOnlyDictionary<ResourceOperationId, JsonElement> OperationPayloads =>
        Operations ?? EmptyOperationPayloads;

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
