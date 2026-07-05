using System.Text.Json;

namespace CloudShell.ControlPlane.Providers;

public interface IConfigurationStoreRuntimeSettingManager
{
    ValueTask<IReadOnlyList<ConfigurationStoreRuntimeSetting>> ListSettingsAsync(
        string resourceId,
        CancellationToken cancellationToken = default);

    ValueTask UpdateSettingsAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeSetting> settings,
        CancellationToken cancellationToken = default);
}

public sealed class ConfigurationStoreRuntimeSettingManager(
    ConfigurationStoreRuntimeOptions options) : IConfigurationStoreRuntimeSettingManager
{
    private readonly ConfigurationStoreRuntimeOptions _options = options;
    private readonly object _gate = new();
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public ValueTask<IReadOnlyList<ConfigurationStoreRuntimeSetting>> ListSettingsAsync(
        string resourceId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            return ValueTask.FromResult<IReadOnlyList<ConfigurationStoreRuntimeSetting>>(
                _options.Settings
                    .OrderBy(setting => setting.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        }
    }

    public ValueTask UpdateSettingsAsync(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeSetting> settings,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(settings);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_gate)
        {
            _options.Settings.Clear();
            foreach (var setting in settings)
            {
                _options.Settings.Add(setting);
            }

            WriteDefinition(resource, _options.Settings.ToArray());
        }

        return ValueTask.CompletedTask;
    }

    private void WriteDefinition(
        ProviderRuntimeResourceContext resource,
        IReadOnlyList<ConfigurationStoreRuntimeSetting> settings)
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
                settings = settings.Select(setting => new
                {
                    setting.Name,
                    setting.Value
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
