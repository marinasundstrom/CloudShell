using System.Text.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _definitionsPath;
    private List<ConfigurationStoreDefinition> _definitions;

    public ConfigurationStore(
        ConfigurationProviderOptions options,
        IHostEnvironment environment)
    {
        _definitionsPath = ResolvePath(options.DefinitionsPath, environment.ContentRootPath);
        _definitions = LoadDefinitions();

        foreach (var configurationStore in options.InitialStores)
        {
            UpsertDefinition(configurationStore, persist: false, replaceExisting: false);
        }

        foreach (var configurationStore in options.DeclaredStores)
        {
            UpsertDefinition(
                configurationStore.Definition,
                persist: false,
                replaceExisting: !configurationStore.Persist ||
                    configurationStore.OverwritePersistedState);
        }
    }

    public IReadOnlyList<ConfigurationStoreDefinition> GetStores()
    {
        lock (_gate)
        {
            return _definitions
                .OrderBy(store => store.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
    }

    public ConfigurationStoreDefinition? GetStore(string id)
    {
        lock (_gate)
        {
            return _definitions.FirstOrDefault(store =>
                string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(ConfigurationStoreDefinition definition)
    {
        lock (_gate)
        {
            var normalized = Normalize(definition);
            var index = _definitions.FindIndex(store =>
                string.Equals(store.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
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
            _definitions.RemoveAll(store =>
                string.Equals(store.Id, id, StringComparison.OrdinalIgnoreCase));
            Persist();
        }
    }

    private List<ConfigurationStoreDefinition> LoadDefinitions()
    {
        if (!File.Exists(_definitionsPath))
        {
            return [];
        }

        var json = File.ReadAllText(_definitionsPath);
        return (JsonSerializer.Deserialize<List<ConfigurationStoreDefinition>>(json, SerializerOptions) ?? [])
            .Select(Normalize)
            .ToList();
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_definitionsPath)!);
        File.WriteAllText(_definitionsPath, JsonSerializer.Serialize(_definitions, SerializerOptions));
    }

    private void UpsertDefinition(
        ConfigurationStoreDefinition definition,
        bool persist,
        bool replaceExisting)
    {
        var normalized = Normalize(definition);
        var index = _definitions.FindIndex(item =>
            string.Equals(item.Id, normalized.Id, StringComparison.OrdinalIgnoreCase));
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

    private static ConfigurationStoreDefinition Normalize(ConfigurationStoreDefinition definition) =>
        definition with
        {
            Id = string.IsNullOrWhiteSpace(definition.Id)
                ? ConfigurationResourceProvider.CreateId(definition.Name)
                : definition.Id.Trim(),
            Name = definition.Name.Trim(),
            AccessToken = string.IsNullOrWhiteSpace(definition.AccessToken)
                ? CreateAccessToken()
                : definition.AccessToken,
            Endpoint = string.IsNullOrWhiteSpace(definition.Endpoint)
                ? null
                : definition.Endpoint.Trim(),
            Entries = definition.Entries
                .Where(entry => !string.IsNullOrWhiteSpace(entry.Name))
                .Select(entry => entry with
                {
                    Name = entry.Name.Trim(),
                    Value = entry.Value ?? string.Empty
                })
                .GroupBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.Last())
                .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray()
        };

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);

    private static string CreateAccessToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
}
