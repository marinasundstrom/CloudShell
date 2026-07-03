using System.Text.Json;

namespace CloudShell.ResourceModel;

public sealed record ResourceRecord(
    string ResourceId,
    string Name,
    string TypeId,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<ResourceReference>? Dependencies = null,
    IReadOnlyDictionary<string, ResourceAttributeValue>? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<string, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null,
    DateTimeOffset? CreatedAt = null,
    DateTimeOffset? LastModifiedAt = null)
{
    public static ResourceRecord FromState(ResourceState state)
    {
        ArgumentNullException.ThrowIfNull(state);

        return new(
            state.EffectiveResourceId,
            state.Name,
            state.TypeId.ToString(),
            state.ProviderId,
            state.DisplayName,
            state.Version,
            state.StartupDependencies.ToArray(),
            state.ResourceAttributeValues.ToDictionary(
                attribute => attribute.Key.ToString(),
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase),
            ClonePayloads(state.CapabilityPayloads),
            ClonePayloads(state.OperationPayloads),
            state.Metadata?.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase),
            state.CreatedAt,
            state.LastModifiedAt);
    }

    public static ResourceRecord FromDefinition(ResourceDefinition definition) =>
        FromState(ResourceState.FromDefinition(definition));

    public ResourceState ToState() =>
        new(
            Name,
            ResourceTypeId.Create(TypeId),
            ResourceId,
            ProviderId,
            DisplayName,
            Version,
            Dependencies,
            Attributes?.ToDictionary(
                attribute => ResourceAttributeId.Create(attribute.Key),
                attribute => attribute.Value) is { } attributes
                    ? new ResourceAttributeValueMap(attributes)
                    : null,
            Capabilities?.ToDictionary(
                payload => ResourceCapabilityId.Create(payload.Key),
                payload => ResourceDefinitionJson.Clone(payload.Value)),
            Operations?.ToDictionary(
                payload => ResourceOperationId.Create(payload.Key),
                payload => ResourceDefinitionJson.Clone(payload.Value)),
            Metadata,
            CreatedAt,
            LastModifiedAt);

    public ResourceDefinition ToDefinition() => ToState().ToDefinition();

    private static IReadOnlyDictionary<string, JsonElement> ClonePayloads<TKey>(
        IReadOnlyDictionary<TKey, JsonElement> payloads)
        where TKey : notnull =>
        payloads.ToDictionary(
            payload => payload.Key.ToString() ?? string.Empty,
            payload => ResourceDefinitionJson.Clone(payload.Value),
            StringComparer.OrdinalIgnoreCase);
}
