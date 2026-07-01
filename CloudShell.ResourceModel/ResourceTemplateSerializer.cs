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

    private static readonly HashSet<string> ResourceDefinitionProperties = new(
        [
            "name",
            "type",
            "typeId",
            "resourceId",
            "providerId",
            "displayName",
            "version",
            "dependsOn",
            "attributes",
            "configuration",
            "capabilities",
            "operations",
            "metadata"
        ],
        StringComparer.OrdinalIgnoreCase);

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
            ResourceTemplateFormat.Json => DeserializeJson<ResourceTemplate>(
                document,
                new ResourceTemplateSerializationContext(IsTemplateRoot: true)),
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
            ResourceTemplateFormat.Json => DeserializeJson<ResourceDefinition>(
                document,
                new ResourceTemplateSerializationContext(IsResourceDefinition: true)),
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
            ResourceTemplateFormat.Json => SerializeJson(
                template,
                new ResourceTemplateSerializationContext(IsTemplateRoot: true)),
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
            ResourceTemplateFormat.Json => SerializeJson(
                definition,
                new ResourceTemplateSerializationContext(IsResourceDefinition: true)),
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

    private static TValue DeserializeJson<TValue>(
        string document,
        ResourceTemplateSerializationContext context)
    {
        using var jsonDocument = JsonDocument.Parse(document);
        var normalizedObject = NormalizeJsonElement(jsonDocument.RootElement, context);
        var json = JsonSerializer.Serialize(normalizedObject, JsonOptions);
        return JsonSerializer.Deserialize<TValue>(json, JsonOptions) ??
            throw new JsonException($"Could not deserialize {typeof(TValue).Name}.");
    }

    private static TValue DeserializeYaml<TValue>(
        string document,
        ResourceTemplateSerializationContext context)
    {
        var yamlObject = YamlDeserializer.Deserialize<object>(document);
        var normalizedObject = NormalizeYamlObject(yamlObject, context);
        var json = JsonSerializer.Serialize(normalizedObject, JsonOptions);
        return JsonSerializer.Deserialize<TValue>(json, JsonOptions) ??
            throw new JsonException($"Could not deserialize {typeof(TValue).Name}.");
    }

    private static string SerializeYaml<TValue>(
        TValue value,
        ResourceTemplateSerializationContext context)
    {
        var json = JsonSerializer.SerializeToElement(value, JsonOptions);
        var yamlObject = ConvertJsonElement(json, context);
        return YamlSerializer.Serialize(yamlObject);
    }

    private static string SerializeJson<TValue>(
        TValue value,
        ResourceTemplateSerializationContext context)
    {
        var json = JsonSerializer.SerializeToElement(value, JsonOptions);
        var documentObject = ConvertJsonElement(json, context);
        return JsonSerializer.Serialize(documentObject, JsonOptions);
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
                        : context.ItemsAreDependencyReferences
                            ? new ResourceTemplateSerializationContext(IsDependencyReference: true)
                        : default))
                .ToArray(),
            _ => value
        };

    private static Dictionary<string, object?> NormalizeYamlMap(
        IDictionary<object, object> map,
        ResourceTemplateSerializationContext context)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hoistedAttributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
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

            if (context.IsResourceDefinition &&
                !IsResourceDefinitionProperty(key))
            {
                hoistedAttributes[key] = NormalizeYamlObject(rawValue, default);
                continue;
            }

            var normalizedKey = context.IsResourceDefinition &&
                string.Equals(key, "type", StringComparison.OrdinalIgnoreCase)
                    ? "typeId"
                    : key;
            var childContext = context.IsTemplateRoot &&
                string.Equals(key, "resources", StringComparison.OrdinalIgnoreCase)
                    ? new ResourceTemplateSerializationContext(ItemsAreResourceDefinitions: true)
                    : context.IsResourceDefinition &&
                    string.Equals(key, "dependsOn", StringComparison.OrdinalIgnoreCase)
                        ? new ResourceTemplateSerializationContext(ItemsAreDependencyReferences: true)
                    : default;
            normalized[normalizedKey] = NormalizeYamlObject(rawValue, childContext);
        }

        MergeHoistedAttributes(normalized, hoistedAttributes);

        if (context.IsTemplateRoot &&
            normalized.ContainsKey("resources") &&
            !normalized.ContainsKey("name"))
        {
            normalized["name"] = DefaultTemplateName;
        }

        NormalizeDependencyReference(normalized, context);
        return normalized;
    }

    private static object? NormalizeJsonElement(
        JsonElement value,
        ResourceTemplateSerializationContext context) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => NormalizeJsonObject(value, context),
            JsonValueKind.Array => value
                .EnumerateArray()
                .Select(item => NormalizeJsonElement(
                    item,
                    context.ItemsAreResourceDefinitions
                        ? new ResourceTemplateSerializationContext(IsResourceDefinition: true)
                        : context.ItemsAreDependencyReferences
                            ? new ResourceTemplateSerializationContext(IsDependencyReference: true)
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

    private static Dictionary<string, object?> NormalizeJsonObject(
        JsonElement value,
        ResourceTemplateSerializationContext context)
    {
        var normalized = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hoistedAttributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var hasTypeId = value.EnumerateObject().Any(property =>
            string.Equals(property.Name, "typeId", StringComparison.OrdinalIgnoreCase));

        foreach (var property in value.EnumerateObject())
        {
            if (context.IsResourceDefinition &&
                string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase) &&
                hasTypeId)
            {
                continue;
            }

            if (context.IsResourceDefinition &&
                !IsResourceDefinitionProperty(property.Name))
            {
                hoistedAttributes[property.Name] = NormalizeJsonElement(property.Value, default);
                continue;
            }

            var normalizedKey = context.IsResourceDefinition &&
                string.Equals(property.Name, "type", StringComparison.OrdinalIgnoreCase)
                    ? "typeId"
                    : property.Name;
            var childContext = context.IsTemplateRoot &&
                string.Equals(property.Name, "resources", StringComparison.OrdinalIgnoreCase)
                    ? new ResourceTemplateSerializationContext(ItemsAreResourceDefinitions: true)
                    : context.IsResourceDefinition &&
                    string.Equals(property.Name, "dependsOn", StringComparison.OrdinalIgnoreCase)
                        ? new ResourceTemplateSerializationContext(ItemsAreDependencyReferences: true)
                        : default;
            normalized[normalizedKey] = NormalizeJsonElement(property.Value, childContext);
        }

        MergeHoistedAttributes(normalized, hoistedAttributes);

        if (context.IsTemplateRoot &&
            normalized.ContainsKey("resources") &&
            !normalized.ContainsKey("name"))
        {
            normalized["name"] = DefaultTemplateName;
        }

        NormalizeDependencyReference(normalized, context);
        return normalized;
    }

    private static void NormalizeDependencyReference(
        Dictionary<string, object?> normalized,
        ResourceTemplateSerializationContext context)
    {
        if (!context.IsDependencyReference ||
            !normalized.ContainsKey("resourceId") ||
            normalized.ContainsKey("relationship"))
        {
            return;
        }

        normalized["relationship"] = ResourceReferenceRelationships.DependsOn.ToString();
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
                        : context.ItemsAreDependencyReferences
                            ? new ResourceTemplateSerializationContext(IsDependencyReference: true)
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

        if (TryConvertResourceReferenceObject(value, out var reference))
        {
            return reference;
        }

        if (context.IsAttributeMap)
        {
            foreach (var property in value.EnumerateObject())
            {
                AddDottedProperty(
                    normalized,
                    property.Name,
                    ConvertJsonElement(
                        GetDocumentAttributeValue(property.Name, property.Value),
                        default));
            }

            return normalized;
        }

        foreach (var property in value.EnumerateObject())
        {
            var key = context.IsResourceDefinition &&
                string.Equals(property.Name, "typeId", StringComparison.OrdinalIgnoreCase)
                    ? "type"
                    : property.Name;

            if (context.IsResourceDefinition &&
                string.Equals(property.Name, "attributes", StringComparison.OrdinalIgnoreCase))
            {
                var convertedAttributes = ConvertJsonElement(
                    property.Value,
                    new ResourceTemplateSerializationContext(IsAttributeMap: true));
                if (convertedAttributes is Dictionary<string, object?> attributes)
                {
                    HoistAttributeProperties(normalized, attributes);
                }

                continue;
            }

            var childContext = context.IsTemplateRoot &&
                string.Equals(property.Name, "resources", StringComparison.OrdinalIgnoreCase)
                    ? new ResourceTemplateSerializationContext(ItemsAreResourceDefinitions: true)
                    : context.IsResourceDefinition &&
                    string.Equals(property.Name, "dependsOn", StringComparison.OrdinalIgnoreCase)
                        ? new ResourceTemplateSerializationContext(ItemsAreDependencyReferences: true)
                        : context.IsResourceDefinition &&
                        string.Equals(property.Name, "attributes", StringComparison.OrdinalIgnoreCase)
                            ? new ResourceTemplateSerializationContext(IsAttributeMap: true)
                    : default;
            normalized[key] = ConvertJsonElement(property.Value, childContext);
        }

        return normalized;
    }

    private static bool IsResourceDefinitionProperty(string key) =>
        ResourceDefinitionProperties.Contains(key);

    private static void MergeHoistedAttributes(
        Dictionary<string, object?> normalized,
        Dictionary<string, object?> hoistedAttributes)
    {
        if (hoistedAttributes.Count == 0)
        {
            return;
        }

        if (!normalized.TryGetValue("attributes", out var existingAttributes) ||
            existingAttributes is not Dictionary<string, object?> attributes)
        {
            attributes = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            normalized["attributes"] = attributes;
        }

        foreach (var (key, value) in hoistedAttributes)
        {
            attributes[key] = value;
        }
    }

    private static void HoistAttributeProperties(
        Dictionary<string, object?> normalized,
        Dictionary<string, object?> attributes)
    {
        Dictionary<string, object?>? wrappedAttributes = null;

        foreach (var (key, value) in attributes)
        {
            if (IsResourceDefinitionProperty(key))
            {
                wrappedAttributes ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                wrappedAttributes[key] = value;
                continue;
            }

            normalized[key] = value;
        }

        if (wrappedAttributes is { Count: > 0 })
        {
            normalized["attributes"] = wrappedAttributes;
        }
    }

    private static bool TryConvertResourceReferenceObject(
        JsonElement value,
        out Dictionary<string, object?> reference)
    {
        reference = [];
        if (!TryGetString(value, "value", out var resourceId) ||
            !TryGetString(value, "relationship", out var relationship) ||
            !TryGetString(value, "addressingMode", out var addressingMode))
        {
            return false;
        }

        if (!string.Equals(addressingMode, ResourceReferenceAddressingModes.ResourceId.ToString(), StringComparison.OrdinalIgnoreCase) ||
            (!string.Equals(relationship, ResourceReferenceRelationships.DependsOn.ToString(), StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(relationship, ResourceReferenceRelationships.Reference.ToString(), StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        reference["resourceId"] = resourceId;
        return true;
    }

    private static JsonElement GetDocumentAttributeValue(
        string attributeId,
        JsonElement value)
    {
        if (string.Equals(attributeId, "logs.sources", StringComparison.OrdinalIgnoreCase) &&
            TryGetSingleObjectProperty(value, "sources", out var sources))
        {
            return sources;
        }

        if (string.Equals(attributeId, "health.checks", StringComparison.OrdinalIgnoreCase) &&
            TryGetSingleObjectProperty(value, "checks", out var checks))
        {
            return checks;
        }

        return value;
    }

    private static bool TryGetSingleObjectProperty(
        JsonElement value,
        string propertyName,
        out JsonElement propertyValue)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.EnumerateObject().Count() == 1 &&
            value.TryGetProperty(propertyName, out propertyValue))
        {
            return true;
        }

        propertyValue = default;
        return false;
    }

    private static void AddDottedProperty(
        Dictionary<string, object?> target,
        string name,
        object? value)
    {
        var segments = name.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            return;
        }

        var current = target;
        for (var index = 0; index < segments.Length - 1; index++)
        {
            if (!current.TryGetValue(segments[index], out var child) ||
                child is not Dictionary<string, object?> childMap)
            {
                childMap = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
                current[segments[index]] = childMap;
            }

            current = childMap;
        }

        current[segments[^1]] = value;
    }

    private static bool TryGetString(
        JsonElement value,
        string propertyName,
        out string propertyValue)
    {
        if (value.ValueKind == JsonValueKind.Object &&
            value.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(property.GetString()))
        {
            propertyValue = property.GetString()!;
            return true;
        }

        propertyValue = string.Empty;
        return false;
    }

    private static bool IsKey(object key, string expected) =>
        string.Equals(key.ToString(), expected, StringComparison.OrdinalIgnoreCase);

    private readonly record struct ResourceTemplateSerializationContext(
        bool IsTemplateRoot = false,
        bool IsResourceDefinition = false,
        bool ItemsAreResourceDefinitions = false,
        bool ItemsAreDependencyReferences = false,
        bool IsDependencyReference = false,
        bool IsAttributeMap = false);
}
