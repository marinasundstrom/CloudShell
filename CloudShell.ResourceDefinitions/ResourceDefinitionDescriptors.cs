using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceClassDefinition(
    ResourceClassId ClassId,
    IReadOnlyDictionary<ResourceAttributeId, string>? Attributes = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyList<ResourceAttributeDefinition>? AttributeDefinitions = null);

public sealed record ResourceTypeDefinition(
    ResourceTypeId TypeId,
    ResourceClassId ClassId,
    string? DefaultProviderId = null,
    IReadOnlyDictionary<ResourceAttributeId, string>? Attributes = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyList<ResourceAttributeDefinition>? AttributeDefinitions = null);

public sealed record ResourceAttributeDefinition(
    ResourceAttributeId Name,
    string? DefaultValue = null,
    bool IsRequired = false,
    string? RequiredMessage = null,
    string? Description = null,
    ResourceAttributeValueShape? ValueShape = null);

public sealed record ResourceAttributeValueShape(
    ResourceAttributeValueKind Kind,
    IReadOnlyList<ResourceAttributeFieldDefinition>? Fields = null,
    ResourceAttributeValueShape? ElementShape = null);

public sealed record ResourceAttributeFieldDefinition(
    string Name,
    ResourceAttributeValueShape ValueShape,
    bool IsRequired = false,
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
