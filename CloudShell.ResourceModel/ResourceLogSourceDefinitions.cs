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
    public const string File = "file";
    public const string PlainText = "plainText";
    public const string JsonConsole = "jsonConsole";
    public const string InMemory = "inMemory";
    public const string FileStorage = "file";
    public const string ProviderDefinedStorage = "providerDefined";
    public const string Read = "read";
    public const string Stream = "stream";
    public const string StructuredFields = "structuredFields";
    public const string ProviderDefault = "providerDefault";
    public const string Default = "default";
    public const string ResourceRunning = "resourceRunning";
    public const string Persisted = "persisted";
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
    string Availability = ResourceLogSourceDefinitionValues.ResourceRunning,
    [property: JsonPropertyName("storage")]
    string? Storage = null)
{
    public static ResourceLogSourceDefinition DefaultConsole(
        string format = ResourceLogSourceDefinitionValues.PlainText) =>
        new(
            "console",
            "Console logs",
            ResourceLogSourceDefinitionValues.ProcessOutput,
            format,
            CreateCapabilities(format),
            Description: "Provider-captured process console output.");

    public static ResourceLogSourceDefinition DefaultContainerConsole(
        string format = ResourceLogSourceDefinitionValues.PlainText) =>
        new(
            "container",
            "Container logs",
            ResourceLogSourceDefinitionValues.Container,
            format,
            CreateCapabilities(format),
            Description: "Provider-captured container stdout and stderr.",
            Availability: ResourceLogSourceDefinitionValues.ResourceRunning);

    public static ResourceLogSourceDefinition File(
        string id,
        string name,
        string path,
        string format = ResourceLogSourceDefinitionValues.PlainText,
        string? description = null) =>
        new(
            id,
            name,
            ResourceLogSourceDefinitionValues.File,
            format,
            CreateCapabilities(format),
            Location: path,
            Description: description ?? "Provider-owned UTF-8 file log.",
            Origin: ResourceLogSourceDefinitionValues.ProviderDefault,
            Purpose: ResourceLogSourceDefinitionValues.Default,
            Availability: ResourceLogSourceDefinitionValues.Persisted,
            Storage: ResourceLogSourceDefinitionValues.FileStorage);

    private static IReadOnlyList<string> CreateCapabilities(string format) =>
        string.Equals(
            format,
            ResourceLogSourceDefinitionValues.JsonConsole,
            StringComparison.OrdinalIgnoreCase)
                ? [
                    ResourceLogSourceDefinitionValues.Read,
                    ResourceLogSourceDefinitionValues.Stream,
                    ResourceLogSourceDefinitionValues.StructuredFields
                ]
                : [
                    ResourceLogSourceDefinitionValues.Read,
                    ResourceLogSourceDefinitionValues.Stream
                ];
}

public static class ResourceLogSourceDefinitionBuilderExtensions
{
    public static TBuilder WithFileLogSource<TBuilder>(
        this TBuilder builder,
        string id,
        string name,
        string path,
        string format = ResourceLogSourceDefinitionValues.PlainText,
        string? description = null)
        where TBuilder : IResourceDefinitionBuilder, IResourceDefinitionAttributeBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(format);

        var existing = builder.AttributeValues.TryGetValue(
            ResourceLogSourceAttributeIds.LogSources,
            out var value)
                ? value.ToObject<ResourceLogSourceDefinitionSet>()?.Sources ?? []
                : [];
        var sources = existing
            .Where(source => !string.Equals(source.Id, id, StringComparison.OrdinalIgnoreCase))
            .Append(ResourceLogSourceDefinition.File(
                id.Trim(),
                name.Trim(),
                path.Trim(),
                format.Trim(),
                description?.Trim()))
            .ToArray();

        builder.SetAttribute(
            ResourceLogSourceAttributeIds.LogSources,
            ResourceAttributeValue.FromObject(new ResourceLogSourceDefinitionSet(sources)));
        return builder;
    }
}
