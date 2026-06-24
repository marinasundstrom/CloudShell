using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceClassDefinition(
    ResourceClassId ClassId,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null);

public sealed record ResourceTypeDefinition(
    ResourceTypeId TypeId,
    ResourceClassId ClassId,
    string? DefaultProviderId = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null);

public sealed record ResourceAttributeDefinition(
    ResourceAttributeValue? DefaultValue = null,
    bool Required = false,
    string? RequiredMessage = null,
    string? Description = null,
    ResourceAttributeValueShape? ValueShape = null,
    bool? ReadOnly = null);

public sealed record ResourceAttributeValueShape(
    ResourceAttributeValueKind Kind,
    IReadOnlyList<ResourceAttributeFieldDefinition>? Fields = null,
    ResourceAttributeValueShape? ElementShape = null);

public sealed record ResourceAttributeFieldDefinition(
    string Name,
    ResourceAttributeValueShape ValueShape,
    bool Required = false,
    string? Description = null);

public enum ResourceAttributeValueKind
{
    String,
    Boolean,
    Integer,
    Decimal,
    Object,
    Array
}

public sealed record ResourceAttributeRequirement(
    ResourceAttributeId Name,
    string? Message = null);

public sealed record ResourceCapabilityDeclaration(
    ResourceCapabilityId Id,
    JsonElement? Payload = null,
    bool IsRequired = false);

public sealed record ResourceOperationDeclaration(
    ResourceOperationId Id,
    JsonElement? Payload = null,
    bool IsEnabled = true,
    bool AllowOverride = true,
    string? DisabledReason = null);

public enum ResourceDefinitionValueSource
{
    ClassDefinition,
    TypeDefinition,
    ResourceState,
    Preset
}
