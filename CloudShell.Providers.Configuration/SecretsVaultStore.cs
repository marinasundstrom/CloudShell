using Microsoft.Extensions.Hosting;
using System.Text.Json;

namespace CloudShell.Providers.Configuration;

public sealed class SecretsVaultStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _definitionsPath;
    private List<SecretsVaultDefinition> _definitions;

    public SecretsVaultStore(
        ConfigurationProviderOptions options,
        IHostEnvironment environment)
    {
        _definitionsPath = ResolvePath(options.SecretsVaultDefinitionsPath, environment.ContentRootPath);
        _definitions = LoadDefinitions();

        foreach (var vault in options.InitialSecretsVaults)
        {
            UpsertDefinition(vault, persist: false, replaceExisting: false);
        }

        foreach (var vault in options.DeclaredSecretsVaults)
        {
            UpsertDefinition(vault.Definition, persist: false, replaceExisting: true);
        }
    }

    public IReadOnlyList<SecretsVaultDefinition> GetVaults()
    {
        lock (_gate)
        {
            return _definitions
                .OrderBy(vault => vault.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public SecretsVaultDefinition? GetVault(string id)
    {
        lock (_gate)
        {
            return _definitions.FirstOrDefault(vault =>
                string.Equals(vault.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(SecretsVaultDefinition definition)
    {
        lock (_gate)
        {
            var normalized = Normalize(definition);
            var index = _definitions.FindIndex(vault =>
                string.Equals(vault.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _definitions[index] = normalized;
            }
            else
            {
                _definitions.Add(normalized);
            }

            Persist();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _definitions.RemoveAll(vault =>
                string.Equals(vault.Id, id, StringComparison.OrdinalIgnoreCase));
            Persist();
        }
    }

    private List<SecretsVaultDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        var json = File.ReadAllText(_definitionsPath);
        return (JsonSerializer.Deserialize<List<SecretsVaultDefinition>>(json, SerializerOptions) ?? [])
            .Select(Normalize)
            .ToList();
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
        File.WriteAllText(_definitionsPath, JsonSerializer.Serialize(_definitions, SerializerOptions));
    }

    private void UpsertDefinition(
        SecretsVaultDefinition definition,
        bool persist,
        bool replaceExisting)
    {
        var normalized = Normalize(definition);
        var index = _definitions.FindIndex(vault =>
            string.Equals(vault.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
        if (index >= 0)
        {
            if (!replaceExisting)
            {
                return;
            }

            if (!Equals(_definitions[index], normalized))
            {
                _definitions[index] = normalized;
                if (persist)
                {
                    Persist();
                }
            }

            return;
        }

        _definitions.Add(normalized);
        if (persist)
        {
            Persist();
        }
    }

    private static SecretsVaultDefinition Normalize(SecretsVaultDefinition definition) =>
        definition with
        {
            Id = string.IsNullOrWhiteSpace(definition.Id)
                ? SecretsVaultProvider.CreateId(definition.Name)
                : definition.Id.Trim(),
            Name = definition.Name.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(definition.Endpoint)
                ? null
                : definition.Endpoint.Trim(),
            Secrets = definition.Secrets
                .Where(secret => !string.IsNullOrWhiteSpace(secret.Name))
                .Select(secret => secret with
                {
                    Name = secret.Name.Trim(),
                    Value = secret.Value ?? string.Empty,
                    Version = string.IsNullOrWhiteSpace(secret.Version) ? null : secret.Version.Trim()
                })
                .GroupBy(
                    secret => $"{secret.Name}\0{secret.Version}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(secret => secret.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(secret => secret.Version, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);

}
