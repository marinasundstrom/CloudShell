using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceDefinition(
    string Name,
    string TypeId,
    string? ResourceId = null,
    string? ProviderId = null,
    string? DisplayName = null,
    string? Version = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyDictionary<string, JsonElement>? Configuration = null,
    IReadOnlyDictionary<string, JsonElement>? Capabilities = null,
    IReadOnlyDictionary<string, JsonElement>? Operations = null,
    IReadOnlyDictionary<string, string>? Metadata = null)
{
    private static readonly IReadOnlyList<string> EmptyList = [];
    private static readonly IReadOnlyDictionary<string, string> EmptyAttributes =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private static readonly IReadOnlyDictionary<string, JsonElement> EmptyPayloads =
        new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);

    public string EffectiveResourceId =>
        string.IsNullOrWhiteSpace(ResourceId) ? $"{TypeId}:{Name}" : ResourceId;

    public IReadOnlyList<string> ResourceDependencies => DependsOn ?? EmptyList;

    public IReadOnlyDictionary<string, string> ResourceAttributes => Attributes ?? EmptyAttributes;

    public IReadOnlyDictionary<string, JsonElement> ConfigurationPayloads => Configuration ?? EmptyPayloads;

    public IReadOnlyDictionary<string, JsonElement> CapabilityPayloads => Capabilities ?? EmptyPayloads;

    public IReadOnlyDictionary<string, JsonElement> OperationPayloads => Operations ?? EmptyPayloads;

    public TConfiguration? GetConfiguration<TConfiguration>(
        string sectionName,
        JsonSerializerOptions? options = null) =>
        ConfigurationPayloads.TryGetValue(sectionName, out var payload)
            ? payload.Deserialize<TConfiguration>(options)
            : default;

    public TCapability? GetCapability<TCapability>(
        string capabilityId,
        JsonSerializerOptions? options = null) =>
        CapabilityPayloads.TryGetValue(capabilityId, out var payload)
            ? payload.Deserialize<TCapability>(options)
            : default;

    public TOperation? GetOperation<TOperation>(
        string operationId,
        JsonSerializerOptions? options = null) =>
        OperationPayloads.TryGetValue(operationId, out var payload)
            ? payload.Deserialize<TOperation>(options)
            : default;
}
