using System.Text.Json;
using CloudShell.Providers.Configuration;
using Microsoft.Extensions.Options;

namespace CloudShell.ConfigurationStoreService;

public sealed class ConfigurationStoreServiceStore(
    IOptions<ConfigurationStoreServiceOptions> options,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _definitionsPath = ResolvePath(
        options.Value.DefinitionsPath,
        environment.ContentRootPath);

    private readonly string? _resourceId = string.IsNullOrWhiteSpace(options.Value.ResourceId)
        ? null
        : options.Value.ResourceId.Trim();

    public ConfigurationStoreDefinition? GetStore(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            (_resourceId is not null &&
             !string.Equals(_resourceId, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return LoadDefinitions().FirstOrDefault(store =>
            string.Equals(store.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<ConfigurationStoreDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(_definitionsPath);
        return JsonSerializer.Deserialize<List<ConfigurationStoreDefinition>>(stream, SerializerOptions) ?? [];
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);
}
