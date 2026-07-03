using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

public sealed record ResourceDefinition(
    string Name,
    ResourceTypeId TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<ResourceReference>? DependsOn = null,
    ResourceAttributeValueMap? Attributes = null,
    IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<ResourceOperationId, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly IReadOnlyList<ResourceReference> EmptyReferences = [];
    private static readonly ResourceAttributeValueMap EmptyAttributeValues =
        new(new Dictionary<ResourceAttributeId, ResourceAttributeValue>());
    private static readonly IReadOnlyDictionary<ResourceCapabilityId, JsonElement> EmptyCapabilityPayloads =
        new Dictionary<ResourceCapabilityId, JsonElement>();
    private static readonly IReadOnlyDictionary<ResourceOperationId, JsonElement> EmptyOperationPayloads =
        new Dictionary<ResourceOperationId, JsonElement>();

    [JsonIgnore]
    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    [JsonIgnore]
    public IReadOnlyList<ResourceReference> StartupDependencies => DependsOn ?? EmptyReferences;

    [JsonIgnore]
    public IReadOnlyList<string> StartupDependencyIds => StartupDependencies
        .Where(dependency => dependency.TryGetDependsOnResourceId(out _))
        .Select(dependency =>
        {
            dependency.TryGetDependsOnResourceId(out var resourceId);
            return resourceId;
        })
        .ToArray();

    [JsonIgnore]
    public IReadOnlyList<ResourceReference> ResourceDependencies => StartupDependencies;

    [JsonIgnore]
    public IReadOnlyList<string> ResourceDependencyIds => StartupDependencyIds;

    [JsonIgnore]
    public ResourceAttributeValueMap ResourceAttributeValues =>
        Attributes ?? EmptyAttributeValues;

    [JsonIgnore]
    public IReadOnlyDictionary<ResourceAttributeId, string> ResourceAttributes =>
        ResourceAttributeValueMaps.ToScalars(Attributes);

    [JsonIgnore]
    public IReadOnlyDictionary<ResourceCapabilityId, JsonElement> CapabilityPayloads =>
        Capabilities ?? EmptyCapabilityPayloads;

    [JsonIgnore]
    public IReadOnlyDictionary<ResourceOperationId, JsonElement> OperationPayloads =>
        Operations ?? EmptyOperationPayloads;

    public TCapability? GetCapability<TCapability>(
        ResourceCapabilityId capabilityId,
        JsonSerializerOptions? options = null) =>
        ResourceAttributeValues.TryGetValue(ResourceAttributeId.Create(capabilityId.ToString()), out var attribute)
            ? attribute.ToObject<TCapability>(options ?? ResourceDefinitionJson.Options)
            : CapabilityPayloads.TryGetValue(capabilityId, out var payload)
                ? payload.Deserialize<TCapability>(options ?? ResourceDefinitionJson.Options)
                : default;

    public TOperation? GetOperation<TOperation>(
        ResourceOperationId operationId,
        JsonSerializerOptions? options = null) =>
        OperationPayloads.TryGetValue(operationId, out var payload)
            ? payload.Deserialize<TOperation>(options ?? ResourceDefinitionJson.Options)
            : default;
}
