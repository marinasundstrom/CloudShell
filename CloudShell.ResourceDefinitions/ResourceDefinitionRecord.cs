using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceDefinitionRecord(
    string ResourceId,
    string Name,
    string TypeId,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<string>? Dependencies = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<string, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<string, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    public static ResourceDefinitionRecord FromDefinition(ResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return new(
            definition.EffectiveResourceId,
            definition.Name,
            definition.TypeId.ToString(),
            definition.ProviderId,
            definition.DisplayName,
            definition.Version,
            definition.ResourceDependencies.ToArray(),
            definition.ResourceAttributes.ToDictionary(
                attribute => attribute.Key.ToString(),
                attribute => attribute.Value,
                StringComparer.OrdinalIgnoreCase),
            ClonePayloads(definition.ConfigurationPayloads),
            ClonePayloads(definition.CapabilityPayloads),
            ClonePayloads(definition.OperationPayloads),
            definition.Metadata?.ToDictionary(
                item => item.Key,
                item => item.Value,
                StringComparer.OrdinalIgnoreCase));
    }

    public ResourceDefinition ToDefinition() =>
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
                attribute => attribute.Value),
            Configuration is null
                ? null
                : new Dictionary<string, JsonElement>(
                    Configuration.Select(payload => new KeyValuePair<string, JsonElement>(
                        payload.Key,
                        ResourceDefinitionJson.Clone(payload.Value))),
                    StringComparer.OrdinalIgnoreCase),
            Capabilities?.ToDictionary(
                payload => ResourceCapabilityId.Create(payload.Key),
                payload => ResourceDefinitionJson.Clone(payload.Value)),
            Operations?.ToDictionary(
                payload => ResourceOperationId.Create(payload.Key),
                payload => ResourceDefinitionJson.Clone(payload.Value)),
            Metadata);

    private static IReadOnlyDictionary<string, JsonElement> ClonePayloads<TKey>(
        IReadOnlyDictionary<TKey, JsonElement> payloads)
        where TKey : notnull =>
        payloads.ToDictionary(
            payload => payload.Key.ToString() ?? string.Empty,
            payload => ResourceDefinitionJson.Clone(payload.Value),
            StringComparer.OrdinalIgnoreCase);
}
