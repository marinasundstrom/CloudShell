using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

public static class ResourceReferenceRelationships
{
    public static readonly ResourceReferenceRelationship Reference = "reference";
    public static readonly ResourceReferenceRelationship DependsOn = "dependsOn";
    public static readonly ResourceReferenceRelationship BelongsTo = "belongsTo";
}

public static class ResourceReferenceAddressingModes
{
    public static readonly ResourceReferenceAddressingMode ResourceId = "resourceId";
    public static readonly ResourceReferenceAddressingMode ProjectedResource = "projectedResource";
    public static readonly ResourceReferenceAddressingMode ProviderNative = "providerNative";
}

[JsonConverter(typeof(ResourceReferenceJsonConverter))]
public sealed record ResourceReference(
    string Value,
    ResourceReferenceRelationship Relationship,
    ResourceReferenceAddressingMode AddressingMode,
    ResourceTypeId? TypeId = null,
    string? ProviderId = null)
{
    public static ResourceReference ResourceId(
        string resourceId,
        ResourceReferenceRelationship relationship,
        ResourceTypeId? typeId = null,
        string? providerId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        return new(
            resourceId.Trim(),
            relationship,
            ResourceReferenceAddressingModes.ResourceId,
            typeId,
            providerId);
    }

    public static ResourceReference DependsOnResourceId(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null) =>
        ResourceId(
            resourceId,
            ResourceReferenceRelationships.DependsOn,
            typeId,
            providerId);

    public static ResourceReference ReferenceResourceId(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null) =>
        ResourceId(
            resourceId,
            ResourceReferenceRelationships.Reference,
            typeId,
            providerId);

    public static ResourceReference BelongsToResourceId(
        string resourceId,
        ResourceTypeId? typeId = null,
        string? providerId = null) =>
        ResourceId(
            resourceId,
            ResourceReferenceRelationships.BelongsTo,
            typeId,
            providerId);

    public bool TryGetResourceId(out string resourceId)
    {
        if (AddressingMode == ResourceReferenceAddressingModes.ResourceId &&
            !string.IsNullOrWhiteSpace(Value))
        {
            resourceId = Value.Trim();
            return true;
        }

        resourceId = string.Empty;
        return false;
    }

    public bool TryGetDependsOnResourceId(out string resourceId)
    {
        if (Relationship == ResourceReferenceRelationships.DependsOn &&
            TryGetResourceId(out resourceId))
        {
            return true;
        }

        resourceId = string.Empty;
        return false;
    }
}

internal sealed class ResourceReferenceJsonConverter : JsonConverter<ResourceReference>
{
    public override ResourceReference Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            return ResourceReference.DependsOnResourceId(reader.GetString() ?? string.Empty);
        }

        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected resource reference object but found '{reader.TokenType}'.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var resourceId = GetString(root, "resourceId");
        var value = resourceId ?? GetString(root, "value");

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new JsonException("Resource reference must specify 'resourceId' or 'value'.");
        }

        var relationship = ResourceReferenceRelationship.Create(
            GetString(root, "relationship") ?? ResourceReferenceRelationships.Reference.ToString());
        var addressingMode = ResourceReferenceAddressingMode.Create(
            GetString(root, "addressingMode") ??
            (resourceId is null
                ? ResourceReferenceAddressingModes.ResourceId.ToString()
                : ResourceReferenceAddressingModes.ResourceId.ToString()));
        var typeId = GetString(root, "typeId") is { } typeIdValue
            ? ResourceTypeId.Create(typeIdValue)
            : (ResourceTypeId?)null;
        var providerId = GetString(root, "providerId");

        return new(value, relationship, addressingMode, typeId, providerId);
    }

    public override void Write(
        Utf8JsonWriter writer,
        ResourceReference value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        writer.WriteString("value", value.Value);
        writer.WriteString("relationship", value.Relationship.ToString());
        writer.WriteString("addressingMode", value.AddressingMode.ToString());

        if (value.TypeId is { } typeId)
        {
            writer.WriteString("typeId", typeId.ToString());
        }

        if (!string.IsNullOrWhiteSpace(value.ProviderId))
        {
            writer.WriteString("providerId", value.ProviderId);
        }

        writer.WriteEndObject();
    }

    private static string? GetString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) &&
        property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
}
