using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

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
    private static readonly ResourceAttributeId HealthChecksAttributeId = ResourceAttributeId.Create("health.checks");
    private static readonly ResourceAttributeId ApplicationArtifactsSourceAttributeId = ResourceAttributeId.Create("artifacts.source");
    private static readonly ResourceAttributeId LogSourcesAttributeId = ResourceAttributeId.Create("logs.sources");

    public override ResourceAttributeValueMap Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartObject)
        {
            throw new JsonException($"Expected resource attribute map object but found '{reader.TokenType}'.");
        }

        using var document = JsonDocument.ParseValue(ref reader);
        if (document.RootElement.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Expected resource attribute map object.");
        }

        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>();
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var value = property.Value.Deserialize<ResourceAttributeValue>(options) ??
                throw new JsonException($"Could not deserialize resource attribute '{property.Name}'.");
            FlattenAttributeValue(SplitAttributePath(property.Name), value, attributes);
        }

        return new ResourceAttributeValueMap(attributes);
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

    private static void FlattenAttributeValue(
        IReadOnlyList<string> path,
        ResourceAttributeValue value,
        Dictionary<ResourceAttributeId, ResourceAttributeValue> attributes)
    {
        if (value.Kind != ResourceAttributeValueKind.Object ||
            value.ObjectValue is null ||
            ShouldTreatAsAttributeValue(path, value.ObjectValue))
        {
            var attributeId = ResourceAttributeId.Create(string.Join('.', path));
            attributes[attributeId] = NormalizeAttributeValue(attributeId, value);
            return;
        }

        foreach (var (name, childValue) in value.ObjectValue)
        {
            FlattenAttributeValue([.. path, name], childValue, attributes);
        }
    }

    private static IReadOnlyList<string> SplitAttributePath(string name)
    {
        var segments = name.Split(
            '.',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? [name] : segments;
    }

    private static bool ShouldTreatAsAttributeValue(
        IReadOnlyList<string> path,
        IReadOnlyDictionary<string, ResourceAttributeValue> objectValue)
    {
        if (path.Count < 2)
        {
            return false;
        }

        if (IsDirectCollectionAttribute(path))
        {
            return true;
        }

        foreach (var (name, childValue) in objectValue)
        {
            if (childValue.Kind == ResourceAttributeValueKind.Array ||
                !IsNamespaceSegment(name))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsNamespaceSegment(string value) =>
        !string.IsNullOrWhiteSpace(value) &&
        char.IsLower(value[0]) &&
        value.All(character => char.IsLetterOrDigit(character));

    private static ResourceAttributeValue NormalizeAttributeValue(
        ResourceAttributeId attributeId,
        ResourceAttributeValue value)
    {
        if (value.Kind != ResourceAttributeValueKind.Array)
        {
            return value;
        }

        if (attributeId == LogSourcesAttributeId)
        {
            return ResourceAttributeValue.Object(new Dictionary<string, ResourceAttributeValue>
            {
                ["sources"] = value
            });
        }

        if (attributeId == HealthChecksAttributeId)
        {
            return ResourceAttributeValue.Object(new Dictionary<string, ResourceAttributeValue>
            {
                ["checks"] = value
            });
        }

        return value;
    }

    private static bool IsDirectCollectionAttribute(IReadOnlyList<string> path)
    {
        var attributeId = ResourceAttributeId.Create(string.Join('.', path));
        return attributeId == LogSourcesAttributeId ||
            attributeId == HealthChecksAttributeId ||
            attributeId == ApplicationArtifactsSourceAttributeId;
    }
}
