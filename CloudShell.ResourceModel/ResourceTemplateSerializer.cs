using System.Text.Json;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace CloudShell.ResourceModel;

public enum ResourceTemplateFormat
{
    Yaml,
    Json
}

public static class ResourceTemplateSerializer
{
    private const string DefaultTemplateName = "local";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true
    };

    private static readonly IDeserializer YamlDeserializer = new DeserializerBuilder()
        .WithAttemptingUnquotedStringTypeDeserialization()
        .Build();

    private static readonly ISerializer YamlSerializer = new SerializerBuilder()
        .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
        .Build();

    public static ResourceTemplate DeserializeTemplate(
        string document,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            throw new JsonException("Resource template document is empty.");
        }

        return format switch
        {
            ResourceTemplateFormat.Json => DeserializeJson<ResourceTemplate>(document),
            ResourceTemplateFormat.Yaml => DeserializeYaml<ResourceTemplate>(
                document,
                new ResourceTemplateSerializationContext(IsTemplateRoot: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static ResourceDefinition DeserializeDefinition(
        string document,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml)
    {
        if (string.IsNullOrWhiteSpace(document))
        {
            throw new JsonException("Resource definition document is empty.");
        }

        return format switch
        {
            ResourceTemplateFormat.Json => DeserializeJson<ResourceDefinition>(document),
            ResourceTemplateFormat.Yaml => DeserializeYaml<ResourceDefinition>(
                document,
                new ResourceTemplateSerializationContext(IsResourceDefinition: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static string SerializeTemplate(
        ResourceTemplate template,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml)
    {
        ArgumentNullException.ThrowIfNull(template);

        return format switch
        {
            ResourceTemplateFormat.Json => JsonSerializer.Serialize(template, JsonOptions),
            ResourceTemplateFormat.Yaml => SerializeYaml(
                template,
                new ResourceTemplateSerializationContext(IsTemplateRoot: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static string SerializeDefinition(
        ResourceDefinition definition,
        ResourceTemplateFormat format = ResourceTemplateFormat.Yaml)
    {
        ArgumentNullException.ThrowIfNull(definition);

        return format switch
        {
            ResourceTemplateFormat.Json => JsonSerializer.Serialize(definition, JsonOptions),
            ResourceTemplateFormat.Yaml => SerializeYaml(
                definition,
                new ResourceTemplateSerializationContext(IsResourceDefinition: true)),
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public static ResourceTemplateFormat GetFormatFromFilePath(string path) =>
        string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase)
            ? ResourceTemplateFormat.Json
            : ResourceTemplateFormat.Yaml;

    private static TValue DeserializeJson<TValue>(string document) =>
        JsonSerializer.Deserialize<TValue>(document, JsonOptions) ??
            throw new JsonException($"Could not deserialize {typeof(TValue).Name}.");

    private static TValue DeserializeYaml<TValue>(
        string document,
        ResourceTemplateSerializationContext context)
    {
        var yamlObject = YamlDeserializer.Deserialize<object>(document);
        var normalizedObject = NormalizeYamlObject(yamlObject, context);
        var json = JsonSerializer.Serialize(normalizedObject, JsonOptions);
        return DeserializeJson<TValue>(json);
    }

    private static string SerializeYaml<TValue>(
        TValue value,
        ResourceTemplateSerializationContext context)
    {
        var json = JsonSerializer.SerializeToElement(value, JsonOptions);
        var yamlObject = ConvertJsonElement(json, context);
        return YamlSerializer.Serialize(yamlObject);
    }

    private static object? NormalizeYamlObject(
        object? value,
        ResourceTemplateSerializationContext context) =>
        value switch
        {
            IDictionary<object, object> map => NormalizeYamlMap(map, context),
            IEnumerable<object> items when value is not string => items
                .Select(item => NormalizeYamlObject(
                    item,
                    context.ItemsAreResourceDefinitions
                        ? new ResourceTemplateSerializationContext(IsResourceDefinition: true)
                        : default))
                .ToArray(),
            _ => value
        };

    private static Dictionary<string, object?> NormalizeYamlMap(
        IDictionary<object, object> map,
        ResourceTemplateSerializationContext context)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasTypeId = map.Keys.Any(key => IsKey(key, "typeId"));

        foreach (var (rawKey, rawValue) in map)
        {
            var key = rawKey.ToString() ?? string.Empty;
            if (context.IsResourceDefinition &&
                string.Equals(key, "type", StringComparison.OrdinalIgnoreCase) &&
                hasTypeId)
            {
                continue;
            }

            var normalizedKey = context.IsResourceDefinition &&
                string.Equals(key, "type", StringComparison.OrdinalIgnoreCase)
                    ? "typeId"
                    : key;
            var childContext = context.IsTemplateRoot &&
                string.Equals(key, "resources", StringComparison.OrdinalIgnoreCase)
                    ? new ResourceTemplateSerializationContext(ItemsAreResourceDefinitions: true)
                    : default;
            normalized[normalizedKey] = NormalizeYamlObject(rawValue, childContext);
        }

        if (context.IsTemplateRoot &&
            normalized.ContainsKey("resources") &&
            !normalized.ContainsKey("name"))
        {
            normalized["name"] = DefaultTemplateName;
        }

        return normalized;
    }

    private static object? ConvertJsonElement(
        JsonElement value,
        ResourceTemplateSerializationContext context) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => ConvertJsonObject(value, context),
            JsonValueKind.Array => value
                .EnumerateArray()
                .Select(item => ConvertJsonElement(
                    item,
                    context.ItemsAreResourceDefinitions
                        ? new ResourceTemplateSerializationContext(IsResourceDefinition: true)
                        : default))
                .ToArray(),
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number when value.TryGetInt64(out var integerValue) => integerValue,
            JsonValueKind.Number when value.TryGetDecimal(out var decimalValue) => decimalValue,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            JsonValueKind.Undefined => null,
            _ => value.GetRawText()
        };

    private static Dictionary<string, object?> ConvertJsonObject(
        JsonElement value,
        ResourceTemplateSerializationContext context)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var property in value.EnumerateObject())
        {
            var key = context.IsResourceDefinition &&
                string.Equals(property.Name, "typeId", StringComparison.OrdinalIgnoreCase)
                    ? "type"
                    : property.Name;
            var childContext = context.IsTemplateRoot &&
                string.Equals(property.Name, "resources", StringComparison.OrdinalIgnoreCase)
                    ? new ResourceTemplateSerializationContext(ItemsAreResourceDefinitions: true)
                    : default;
            normalized[key] = ConvertJsonElement(property.Value, childContext);
        }

        return normalized;
    }

    private static bool IsKey(object key, string expected) =>
        string.Equals(key.ToString(), expected, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ResourceTemplateSerializationContext(
        bool IsTemplateRoot = false,
        bool IsResourceDefinition = false,
        bool ItemsAreResourceDefinitions = false);
}
