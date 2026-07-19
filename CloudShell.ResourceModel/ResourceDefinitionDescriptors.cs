using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

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
    [property: JsonPropertyName("collection")]
    ResourceAttributeCollectionDefinition? CollectionOptions = null,
    bool? ReadOnly = null,
    ResourceAttributeMutability? Mutability = null,
    string? Path = null,
    string? DisplayName = null,
    IReadOnlyList<string>? Aliases = null,
    string? Format = null,
    IReadOnlyList<ResourceAttributeValue>? AllowedValues = null)
{
    [JsonIgnore]
    public ResourceAttributeValueShapeId? ItemShapeId => IsCollection ? ValueShapeId : null;

    public static ResourceAttributeDefinition Collection(
        ResourceAttributeValueType itemType,
        ResourceAttributeValueShape? itemShape = null,
        ResourceAttributeValueShapeId? itemShapeId = null,
        ResourceAttributeCollectionDefinition? collection = null,
        string? description = null,
        bool required = false,
        string? requiredMessage = null,
        string? format = null,
        IReadOnlyList<ResourceAttributeValue>? allowedValues = null,
        bool? readOnly = null,
        ResourceAttributeMutability? mutability = null,
        string? path = null,
        string? displayName = null,
        IReadOnlyList<string>? aliases = null) =>
        new(
            Required: required,
            RequiredMessage: requiredMessage,
            Description: description,
            ValueType: itemType,
            Format: format,
            AllowedValues: allowedValues,
            ValueShape: itemShape,
            ValueShapeId: itemShapeId,
            IsCollection: true,
            CollectionOptions: collection,
            ReadOnly: readOnly,
            Mutability: mutability,
            Path: path,
            DisplayName: displayName,
            Aliases: aliases);
}

public sealed record ResourceAttributeValueShapeDefinition(
    ResourceAttributeValueShape Shape,
    string? Description = null);

public sealed record ResourceAttributeCollectionDefinition(
    int? MinSize = null,
    int? MaxSize = null);

public sealed record ResourceAttributeValueShape(
    IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeDefinition>? Attributes = null,
    bool? AllowAdditionalProperties = null,
    ResourceAttributeDefinition? AdditionalProperties = null);

public enum ResourceAttributeValueType
{
    String,
    Boolean,
    Integer,
    FloatingPoint,
    ComplexType,
    ResourceReference
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
    CapabilityDefinition,
    ResourceState,
    Preset
}
