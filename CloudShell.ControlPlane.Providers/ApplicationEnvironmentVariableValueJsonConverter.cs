using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CloudShell.ControlPlane.Providers;

internal sealed class ApplicationEnvironmentVariableValueJsonConverter : JsonConverter<string?>
{
    public override string? Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options) =>
        reader.TokenType switch
        {
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number when reader.TryGetInt64(out var integerValue) =>
                integerValue.ToString(CultureInfo.InvariantCulture),
            JsonTokenType.Number => reader.GetDecimal().ToString(CultureInfo.InvariantCulture),
            JsonTokenType.True => bool.TrueString.ToLower(CultureInfo.InvariantCulture),
            JsonTokenType.False => bool.FalseString.ToLower(CultureInfo.InvariantCulture),
            JsonTokenType.Null => null,
            _ => throw new JsonException(
                $"Unsupported environment variable value token '{reader.TokenType}'.")
        };

    public override void Write(
        Utf8JsonWriter writer,
        string? value,
        JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStringValue(value);
    }
}
