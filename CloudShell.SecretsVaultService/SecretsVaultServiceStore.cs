using Microsoft.Extensions.Options;
using System.Text.Json;

namespace CloudShell.SecretsVaultService;

public sealed class SecretsVaultServiceStore(
    IOptions<SecretsVaultServiceOptions> options,
    IHostEnvironment environment)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly string _definitionsPath = ResolvePath(
        options.Value.DefinitionsPath,
        environment.ContentRootPath);

    private readonly string? _resourceId = string.IsNullOrWhiteSpace(options.Value.ResourceId)
        ? null
        : options.Value.ResourceId.Trim();

    public SecretsVaultDefinition? GetVault(string resourceId)
    {
        if (string.IsNullOrWhiteSpace(resourceId) ||
            (_resourceId is not null &&
             !string.Equals(_resourceId, resourceId, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        return LoadDefinitions().FirstOrDefault(vault =>
            string.Equals(vault.Id, resourceId, StringComparison.OrdinalIgnoreCase));
    }

    private IReadOnlyList<SecretsVaultDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        using var stream = File.OpenRead(_definitionsPath);
        return JsonSerializer.Deserialize<List<SecretsVaultDefinition>>(stream, SerializerOptions) ?? [];
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);
}
