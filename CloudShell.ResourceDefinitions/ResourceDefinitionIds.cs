using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceDefinitions;

[JsonConverter(typeof(ResourceClassIdJsonConverter))]
public readonly record struct ResourceClassId(string Value)
{
    public static ResourceClassId Create(string value) => new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceClassId(string value) => Create(value);

    public static implicit operator string(ResourceClassId value) => value.ToString();
}

[JsonConverter(typeof(ResourceTypeIdJsonConverter))]
public readonly record struct ResourceTypeId(string Value)
{
    public static ResourceTypeId Create(string value) => new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceTypeId(string value) => Create(value);

    public static implicit operator string(ResourceTypeId value) => value.ToString();
}

[JsonConverter(typeof(ResourceAttributeIdJsonConverter))]
public readonly record struct ResourceAttributeId(string Value)
{
    public static ResourceAttributeId Create(string value) => new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceAttributeId(string value) => Create(value);

    public static implicit operator string(ResourceAttributeId value) => value.ToString();
}

[JsonConverter(typeof(ResourceAttributeValueShapeIdJsonConverter))]
public readonly record struct ResourceAttributeValueShapeId(string Value)
{
    public static ResourceAttributeValueShapeId Create(string value) =>
        new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceAttributeValueShapeId(string value) => Create(value);

    public static implicit operator string(ResourceAttributeValueShapeId value) => value.ToString();
}

[JsonConverter(typeof(ResourceCapabilityIdJsonConverter))]
public readonly record struct ResourceCapabilityId(string Value)
{
    public static ResourceCapabilityId Create(string value) => new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceCapabilityId(string value) => Create(value);

    public static implicit operator string(ResourceCapabilityId value) => value.ToString();
}

[JsonConverter(typeof(ResourceOperationIdJsonConverter))]
public readonly record struct ResourceOperationId(string Value)
{
    public static ResourceOperationId Create(string value) => new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceOperationId(string value) => Create(value);

    public static implicit operator string(ResourceOperationId value) => value.ToString();
}

[JsonConverter(typeof(ResourceReferenceRelationshipJsonConverter))]
public readonly record struct ResourceReferenceRelationship(string Value)
{
    public static ResourceReferenceRelationship Create(string value) =>
        new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceReferenceRelationship(string value) => Create(value);

    public static implicit operator string(ResourceReferenceRelationship value) => value.ToString();
}

[JsonConverter(typeof(ResourceReferenceAddressingModeJsonConverter))]
public readonly record struct ResourceReferenceAddressingMode(string Value)
{
    public static ResourceReferenceAddressingMode Create(string value) =>
        new(ResourceDefinitionId.Normalize(value, nameof(value)));

    public override string ToString() => Value ?? string.Empty;

    public static implicit operator ResourceReferenceAddressingMode(string value) => Create(value);

    public static implicit operator string(ResourceReferenceAddressingMode value) => value.ToString();
}

internal static class ResourceDefinitionId
{
    public static string Normalize(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Resource definition identifiers cannot be empty.", parameterName);
        }

        return value.Trim();
    }
}

internal sealed class ResourceClassIdJsonConverter : ResourceDefinitionIdJsonConverter<ResourceClassId>
{
    protected override ResourceClassId Create(string value) => ResourceClassId.Create(value);
}

internal sealed class ResourceTypeIdJsonConverter : ResourceDefinitionIdJsonConverter<ResourceTypeId>
{
    protected override ResourceTypeId Create(string value) => ResourceTypeId.Create(value);
}

internal sealed class ResourceAttributeIdJsonConverter : ResourceDefinitionIdJsonConverter<ResourceAttributeId>
{
    protected override ResourceAttributeId Create(string value) => ResourceAttributeId.Create(value);
}

internal sealed class ResourceAttributeValueShapeIdJsonConverter :
    ResourceDefinitionIdJsonConverter<ResourceAttributeValueShapeId>
{
    protected override ResourceAttributeValueShapeId Create(string value) =>
        ResourceAttributeValueShapeId.Create(value);
}

internal sealed class ResourceCapabilityIdJsonConverter : ResourceDefinitionIdJsonConverter<ResourceCapabilityId>
{
    protected override ResourceCapabilityId Create(string value) => ResourceCapabilityId.Create(value);
}

internal sealed class ResourceOperationIdJsonConverter : ResourceDefinitionIdJsonConverter<ResourceOperationId>
{
    protected override ResourceOperationId Create(string value) => ResourceOperationId.Create(value);
}

internal sealed class ResourceReferenceRelationshipJsonConverter :
    ResourceDefinitionIdJsonConverter<ResourceReferenceRelationship>
{
    protected override ResourceReferenceRelationship Create(string value) =>
        ResourceReferenceRelationship.Create(value);
}

internal sealed class ResourceReferenceAddressingModeJsonConverter :
    ResourceDefinitionIdJsonConverter<ResourceReferenceAddressingMode>
{
    protected override ResourceReferenceAddressingMode Create(string value) =>
        ResourceReferenceAddressingMode.Create(value);
}

internal abstract class ResourceDefinitionIdJsonConverter<TId> : JsonConverter<TId>
{
    public override TId Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType == JsonTokenType.String
            ? Create(reader.GetString() ?? string.Empty)
            : throw new JsonException($"Expected a string value for {typeof(TId).Name}.");

    public override void Write(
        Utf8JsonWriter writer,
        TId value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value?.ToString());

    public override TId ReadAsPropertyName(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        Create(reader.GetString() ?? string.Empty);

    public override void WriteAsPropertyName(
        Utf8JsonWriter writer,
        TId value,
        JsonSerializerOptions options) =>
        writer.WritePropertyName(value?.ToString() ?? string.Empty);

    protected abstract TId Create(string value);
}
