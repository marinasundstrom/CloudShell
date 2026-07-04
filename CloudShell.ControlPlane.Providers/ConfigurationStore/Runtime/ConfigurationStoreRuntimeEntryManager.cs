using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IConfigurationStoreRuntimeEntryManager
{
    ValueTask<IReadOnlyList<ConfigurationStoreRuntimeEntry>> ListEntriesAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    ValueTask UpdateEntriesAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeEntry> entries,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationStoreRuntimeEntryManager(
    ConfigurationStoreRuntimeOptions options) : IConfigurationStoreRuntimeEntryManager
{
    private readonly ConfigurationStoreRuntimeOptions _options = options;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<IReadOnlyList<ConfigurationStoreRuntimeEntry>> ListEntriesAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ConfigurationStoreRuntimeEntry>>(
                _options.Entries
                    .OrderBy(entry => entry.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask UpdateEntriesAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeEntry> entries,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(entries);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _options.Entries.Clear();
            foreach (var entry in entries)
            {
                _options.Entries.Add(entry);
            }

            WriteDefinition(resource, _options.Entries.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    private void WriteDefinition(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeEntry> entries)
    {
        var directory = Path.Combine(_options.DefinitionsDirectory, SanitizeFileName(resource.ResourceId));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "configuration-stores.json");
        using var stream = File.Create(path);
        JsonSerializer.Serialize(stream, new[]
        {
            new
            {
                id = resource.ResourceId,
                name = resource.Name,
                displayName = resource.DisplayName,
                endpoint = resource.Endpoint,
                entries = entries.Select(entry => new
                {
                    entry.Name,
                    entry.Value
                }).ToArray(),
                healthChecks = Array.Empty<object>()
            }
        }, SerializerOptions);
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return string.Concat(value.Select(character =>
            invalid.Contains(character) || character is ':' or '/' or '\\'
                ? '_'
                : character));
    }
}
