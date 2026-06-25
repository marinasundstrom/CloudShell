using System.Text.Json;

namespace CloudShell.ResourceDefinitions;

public sealed record ResourceClassDefinition(
    ResourceClassId ClassId,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null,
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? AttributeValueShapes = null);

public sealed record ResourceTypeDefinition(
    ResourceTypeId TypeId,
    ResourceClassId ClassId,
    string? DefaultProviderId = null,
    IReadOnlyList<ResourceAttributeRequirement>? RequiredAttributes = null,
    IReadOnlyList<ResourceCapabilityDeclaration>? Capabilities = null,
    IReadOnlyList<ResourceOperationDeclaration>? Operations = null,
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null,
    IReadOnlyDictionary<ResourceAttributeValueShapeId, ResourceAttributeValueShapeDefinition>? AttributeValueShapes = null);

public sealed record ResourceAttributeDefinition(
    ResourceAttributeValue? DefaultValue = null,
    bool Required = false,
    string? RequiredMessage = null,
    string? Description = null,
    ResourceAttributeValueType? ValueType = null,
    ResourceAttributeValueShape? ValueShape = null,
    ResourceAttributeValueShapeId? ValueShapeId = null,
    bool IsCollection = false,
    ResourceAttributeCollectionDefinition? Collection = null,
    bool? ReadOnly = null,
    ResourceAttributeMutability? Mutability = null);

public sealed record ResourceAttributeValueShapeDefinition(
    ResourceAttributeValueShape Shape,
    string? Description = null);

public sealed record ResourceAttributeCollectionDefinition(
    int? MinSize = null,
    int? MaxSize = null);

public sealed record ResourceAttributeValueShape(
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null);

public enum ResourceAttributeValueType
{
    String,
    Boolean,
    Integer,
    FloatingPoint,
    ComplexType
}

public enum ResourceAttributeValueKind
{
    String,
    Boolean,
    Integer,
    Decimal,
    Object,
    Array
}

public enum ResourceAttributeMutability
{
    CallerManaged,
    ProviderManaged
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
