using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceDefinitions;

[JsonConverter(typeof(ResourceAttributeValueMapJsonConverter))]
public sealed class ResourceAttributeValueMap : IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>
{
    private readonly IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> _attributes;

    public ResourceAttributeValueMap(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue> attributes)
    {
        ArgumentNullException.ThrowIfNull(attributes);
        _attributes = attributes.ToDictionary(
            attribute => attribute.Key,
            attribute => attribute.Value);
    }

    public ResourceAttributeValue this[ResourceAttributeId key] => _attributes[key];

    public IEnumerable<ResourceAttributeId> Keys => _attributes.Keys;

    public IEnumerable<ResourceAttributeValue> Values => _attributes.Values;

    public int Count => _attributes.Count;

    public bool ContainsKey(ResourceAttributeId key) => _attributes.ContainsKey(key);

    public IEnumerator<KeyValuePair<ResourceAttributeId, ResourceAttributeValue>> GetEnumerator() =>
        _attributes.GetEnumerator();

    public bool TryGetValue(ResourceAttributeId key, out ResourceAttributeValue value) =>
        _attributes.TryGetValue(key, out value!);

    public TValue? GetObject<TValue>(
        ResourceAttributeId key,
        JsonSerializerOptions? options = null) =>
        TryGetValue(key, out var value) ? value.ToObject<TValue>(options) : default;

    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() =>
        GetEnumerator();

    public static implicit operator ResourceAttributeValueMap?(
        Dictionary<ResourceAttributeId, string>? attributes) =>
        ResourceAttributeValueMaps.FromScalars(attributes);

    public static implicit operator ResourceAttributeValueMap?(
        Dictionary<ResourceAttributeId, ResourceAttributeValue>? attributes) =>
        attributes is null ? null : new(attributes);
}

public static class ResourceAttributeValueMaps
{
    public static ResourceAttributeValueMap? FromScalars(
        IReadOnlyDictionary<ResourceAttributeId, string>? attributes)
    {
        if (attributes is null)
        {
            return null;
        }

        return new ResourceAttributeValueMap(
            attributes.ToDictionary(
                attribute => attribute.Key,
                attribute => ResourceAttributeValue.String(attribute.Value)));
    }

    public static IReadOnlyDictionary<ResourceAttributeId, string> ToScalars(
        IReadOnlyDictionary<ResourceAttributeId, ResourceAttributeValue>? attributes)
    {
        if (attributes is null)
        {
            return new Dictionary<ResourceAttributeId, string>();
        }

        return attributes
            .Where(attribute => attribute.Value.TryGetScalarString(out _))
            .ToDictionary(
                attribute => attribute.Key,
                attribute =>
                {
                    attribute.Value.TryGetScalarString(out var value);
                    return value;
            });
    }
}

internal sealed class ResourceAttributeValueMapJsonConverter :
    JsonConverter<ResourceAttributeValueMap>
{
    public override ResourceAttributeValueMap Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected resource attribute map object but found '{reader.TokenType}'.");
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>();

        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject)
            {
                return new ResourceAttributeValueMap(attributes);
            }

            if (reader.TokenType != JsonTokenType.PropertyName)
            {
                throw new JsonException($"Expected resource attribute property but found '{reader.TokenType}'.");
            }

            var name = reader.GetString() ?? string.Empty;
            reader.Read();
            var value = JsonSerializer.Deserialize<ResourceAttributeValue>(ref reader, options) ??
                throw new JsonException($"Could not deserialize resource attribute '{name}'.");
            attributes[ResourceAttributeId.Create(name)] = value;
        }

        throw new JsonException("Resource attribute map JSON ended before the object was closed.");
    }

    public override void Write(
        Utf8JsonWriter writer,
        ResourceAttributeValueMap value,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();

        foreach (var (name, attributeValue) in value)
        {
            writer.WritePropertyName(name.ToString());
            JsonSerializer.Serialize(writer, attributeValue, options);
        }

        writer.WriteEndObject();
    }
}
