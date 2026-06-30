using System.Text.Json;

namespace CloudShell.ResourceModel;

public static class ResourceDefinitionJson
{
    public static JsonElement EmptyObject { get; } =
        JsonDocument.Parse("{}").RootElement.Clone();

    public static JsonElement FromValue<TValue>(TValue value, JsonSerializerOptions? options = null) =>
        JsonSerializer.SerializeToElement(value, options).Clone();

    public static JsonElement Clone(JsonElement value) => value.Clone();
}
