using System.Globalization;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

[JsonConverter(typeof(ResourceAttributeValueJsonConverter))]
public sealed record ResourceAttributeValue
{
    private static readonly JsonSerializerOptions DefaultObjectMappingOptions = new(JsonSerializerOptions.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private ResourceAttributeValue(
        ResourceAttributeValueKind kind,
        string? stringValue = null,
        bool? booleanValue = null,
        long? integerValue = null,
        decimal? decimalValue = null,
        IReadOnlyDictionary<string, ResourceAttributeValue>? objectValue = null,
        IReadOnlyList<ResourceAttributeValue>? arrayValue = null)
    {
        Kind = kind;
        StringValue = stringValue;
        BooleanValue = booleanValue;
        IntegerValue = integerValue;
        DecimalValue = decimalValue;
        ObjectValue = objectValue;
        ArrayValue = arrayValue;
    }

    public ResourceAttributeValueKind Kind { get; }

    public string? StringValue { get; }

    public bool? BooleanValue { get; }

    public long? IntegerValue { get; }

    public decimal? DecimalValue { get; }

    public IReadOnlyDictionary<string, ResourceAttributeValue>? ObjectValue { get; }

    public IReadOnlyList<ResourceAttributeValue>? ArrayValue { get; }

    public static ResourceAttributeValue String(string value) =>
        new(ResourceAttributeValueKind.String, stringValue: value);

    public static ResourceAttributeValue Boolean(bool value) =>
        new(ResourceAttributeValueKind.Boolean, booleanValue: value);

    public static ResourceAttributeValue Integer(long value) =>
        new(ResourceAttributeValueKind.Integer, integerValue: value);

    public static ResourceAttributeValue Decimal(decimal value) =>
        new(ResourceAttributeValueKind.Decimal, decimalValue: value);

    public static ResourceAttributeValue Object(
        IReadOnlyDictionary<string, ResourceAttributeValue> value) =>
        new(ResourceAttributeValueKind.Object, objectValue: value);

    public static ResourceAttributeValue Array(
        IReadOnlyList<ResourceAttributeValue> value) =>
        new(ResourceAttributeValueKind.Array, arrayValue: value);

    public static ResourceAttributeValue ResourceReference(
        ResourceReference value) =>
        FromObject(value);

    public static ResourceAttributeValue FromObject<TValue>(
        TValue value,
        JsonSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(value);

        var serializerOptions = options ?? DefaultObjectMappingOptions;
        using var document = JsonSerializer.SerializeToDocument(
            value,
            serializerOptions);
        return document.RootElement.Deserialize<ResourceAttributeValue>(
            serializerOptions) ??
            throw new JsonException($"Could not map {typeof(TValue).Name} to a resource attribute value.");
    }

    public static implicit operator ResourceAttributeValue(string value) => String(value);

    public static implicit operator ResourceAttributeValue(bool value) => Boolean(value);

    public static implicit operator ResourceAttributeValue(int value) => Integer(value);

    public static implicit operator ResourceAttributeValue(long value) => Integer(value);

    public static implicit operator ResourceAttributeValue(decimal value) => Decimal(value);

    public TValue? ToObject<TValue>(
        JsonSerializerOptions? options = null)
    {
        var serializerOptions = options ?? DefaultObjectMappingOptions;
        return JsonSerializer.SerializeToElement(
                this,
                serializerOptions)
            .Deserialize<TValue>(serializerOptions);
    }

    public bool TryGetResourceReference(
        [NotNullWhen(true)] out ResourceReference? reference)
    {
        if (Kind != ResourceAttributeValueKind.Object ||
            ObjectValue is null)
        {
            reference = null;
            return false;
        }

        var hasValue = TryGetObjectString(ObjectValue, "value", out var value);
        var hasResourceId = TryGetObjectString(ObjectValue, "resourceId", out var resourceId);
        if (!hasValue && !hasResourceId)
        {
            reference = null;
            return false;
        }

        if (!TryGetObjectString(ObjectValue, "relationship", out var relationship))
        {
            relationship = ResourceReferenceRelationships.Reference.ToString();
        }

        if (!TryGetObjectString(ObjectValue, "addressingMode", out var addressingMode))
        {
            addressingMode = hasResourceId
                ? ResourceReferenceAddressingModes.ResourceId.ToString()
                : null;
        }

        if (string.IsNullOrWhiteSpace(addressingMode))
        {
            reference = null;
            return false;
        }

        reference = new(
            hasResourceId ? resourceId! : value!,
            ResourceReferenceRelationship.Create(relationship),
            ResourceReferenceAddressingMode.Create(addressingMode),
            TryGetObjectString(ObjectValue, "typeId", out var typeId)
                ? ResourceTypeId.Create(typeId)
                : (ResourceTypeId?)null,
            TryGetObjectString(ObjectValue, "providerId", out var providerId)
                ? providerId
                : null);
        return true;
    }

    public bool TryGetScalarString(out string value)
    {
        switch (Kind)
        {
            case ResourceAttributeValueKind.String:
                value = StringValue ?? string.Empty;
                return true;
            case ResourceAttributeValueKind.Boolean:
                value = BooleanValue.GetValueOrDefault()
                    ? bool.TrueString.ToLowerInvariant()
                    : bool.FalseString.ToLowerInvariant();
                return true;
            case ResourceAttributeValueKind.Integer:
                value = IntegerValue.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
                return true;
            case ResourceAttributeValueKind.Decimal:
                value = DecimalValue.GetValueOrDefault().ToString(CultureInfo.InvariantCulture);
                return true;
            default:
                value = string.Empty;
                return false;
        }
    }

    private static bool TryGetObjectString(
        IReadOnlyDictionary<string, ResourceAttributeValue> value,
        string propertyName,
        [NotNullWhen(true)] out string? propertyValue)
    {
        if (value.TryGetValue(propertyName, out var attributeValue) &&
            attributeValue.Kind == ResourceAttributeValueKind.String &&
            !string.IsNullOrWhiteSpace(attributeValue.StringValue))
        {
            propertyValue = attributeValue.StringValue;
            return true;
        }

        propertyValue = null;
        return false;
    }
}

internal sealed class ResourceAttributeValueJsonConverter : JsonConverter<ResourceAttributeValue>
{
    public override ResourceAttributeValue Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => ResourceAttributeValue.String(reader.GetString() ?? string.Empty),
            JsonTokenType.True => ResourceAttributeValue.Boolean(true),
            JsonTokenType.False => ResourceAttributeValue.Boolean(false),
            JsonTokenType.Number when reader.TryGetInt64(out var integerValue) =>
                ResourceAttributeValue.Integer(integerValue),
            JsonTokenType.Number => ResourceAttributeValue.Decimal(reader.GetDecimal()),
            JsonTokenType.StartObject => ResourceAttributeValue.Object(
                JsonSerializer.Deserialize<Dictionary<string, ResourceAttributeValue>>(ref reader, options) ?? []),
            JsonTokenType.StartArray => ResourceAttributeValue.Array(
                JsonSerializer.Deserialize<List<ResourceAttributeValue>>(ref reader, options) ?? []),
            _ => throw new JsonException($"Unsupported resource attribute value token '{reader.TokenType}'.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        ResourceAttributeValue value,
        JsonSerializerOptions options)
    {
        switch (value.Kind)
        {
            case ResourceAttributeValueKind.String:
                writer.WriteStringValue(value.StringValue);
                break;
            case ResourceAttributeValueKind.Boolean:
                writer.WriteBooleanValue(value.BooleanValue.GetValueOrDefault());
                break;
            case ResourceAttributeValueKind.Integer:
                writer.WriteNumberValue(value.IntegerValue.GetValueOrDefault());
                break;
            case ResourceAttributeValueKind.Decimal:
                writer.WriteNumberValue(value.DecimalValue.GetValueOrDefault());
                break;
            case ResourceAttributeValueKind.Object:
                JsonSerializer.Serialize(writer, value.ObjectValue ?? new Dictionary<string, ResourceAttributeValue>(), options);
                break;
            case ResourceAttributeValueKind.Array:
                JsonSerializer.Serialize(writer, value.ArrayValue ?? [], options);
                break;
            default:
                throw new JsonException($"Unsupported resource attribute value kind '{value.Kind}'.");
        }
    }
}
