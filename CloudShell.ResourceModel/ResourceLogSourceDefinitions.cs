using System.Text.Json.Serialization;

namespace CloudShell.ResourceModel;

public static class ResourceLogSourceCapabilityIds
{
    public static readonly ResourceCapabilityId LogSources = "logs.sources";
}

public static class ResourceLogSourceAttributeIds
{
    public static readonly ResourceAttributeId LogSources = "logs.sources";
}

public static class ResourceLogSourceDefinitionValues
{
    public const string ProcessOutput = "processOutput";
    public const string Container = "container";
    public const string PlainText = "plainText";
    public const string JsonConsole = "jsonConsole";
    public const string Read = "read";
    public const string Stream = "stream";
    public const string ProviderDefault = "providerDefault";
    public const string Default = "default";
    public const string ResourceRunning = "resourceRunning";
}

public sealed record ResourceLogSourceDefinitionSet(
    [property: JsonPropertyName("sources")]
    IReadOnlyList<ResourceLogSourceDefinition>? Sources = null)
{
    public static ResourceLogSourceDefinitionSet DefaultConsole(
        string format = ResourceLogSourceDefinitionValues.PlainText) =>
        new(
            [
                ResourceLogSourceDefinition.DefaultConsole(format)
            ]);
}

public sealed record ResourceLogSourceDefinition(
    [property: JsonPropertyName("id")]
    string Id,
    [property: JsonPropertyName("name")]
    string Name,
    [property: JsonPropertyName("kind")]
    string Kind,
    [property: JsonPropertyName("format")]
    string Format = ResourceLogSourceDefinitionValues.PlainText,
    [property: JsonPropertyName("capabilities")]
    IReadOnlyList<string>? Capabilities = null,
    [property: JsonPropertyName("location")]
    string? Location = null,
    [property: JsonPropertyName("producerResourceId")]
    string? ProducerResourceId = null,
    [property: JsonPropertyName("description")]
    string? Description = null,
    [property: JsonPropertyName("origin")]
    string Origin = ResourceLogSourceDefinitionValues.ProviderDefault,
    [property: JsonPropertyName("purpose")]
    string Purpose = ResourceLogSourceDefinitionValues.Default,
    [property: JsonPropertyName("availability")]
    string Availability = ResourceLogSourceDefinitionValues.ResourceRunning)
{
    public static ResourceLogSourceDefinition DefaultConsole(
        string format = ResourceLogSourceDefinitionValues.PlainText) =>
        new(
            "console",
            "Console logs",
            ResourceLogSourceDefinitionValues.ProcessOutput,
            format,
            [
                ResourceLogSourceDefinitionValues.Read,
                ResourceLogSourceDefinitionValues.Stream
            ],
            Description: "Provider-captured process console output.");

    public static ResourceLogSourceDefinition DefaultContainerConsole(
        string format = ResourceLogSourceDefinitionValues.PlainText) =>
        new(
            "container",
            "Container logs",
            ResourceLogSourceDefinitionValues.Container,
            format,
            [
                ResourceLogSourceDefinitionValues.Read,
                ResourceLogSourceDefinitionValues.Stream
            ],
            Description: "Provider-captured container stdout and stderr.",
            Availability: ResourceLogSourceDefinitionValues.ResourceRunning);
}
