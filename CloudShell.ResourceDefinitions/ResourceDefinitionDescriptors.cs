using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceClassDefinition(
    ResourceClass ResourceClass,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null);

public sealed record ResourceTypeDefinition(
    string TypeId,
    ResourceClass ResourceClass,
    string? DefaultProviderId = null,
    IReadOnlyDictionary<string, string>? Attributes = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null);

public sealed record ResourceAttributeRequirement(
    string Name,
    string? Message = null);

public sealed record ResourceCapabilityDeclaration(
    string Id,
    JsonElement? Payload = null,
    bool IsRequired = false);

public sealed record ResourceOperationDeclaration(
    string Id,
    JsonElement? Payload = null,
    bool IsEnabled = true,
    bool AllowOverride = true,
    string? DisabledReason = null);

public enum ResourceDefinitionValueSource
{
    ClassDefinition,
    TypeDefinition,
    ResourceDefinition,
    Preset
}
