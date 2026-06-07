using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceOrchestratorSelectionStore
{
    public const int DefaultHealthCheckIntervalSeconds = 10;
    public const int MinimumHealthCheckIntervalSeconds = 1;
    public const int MaximumHealthCheckIntervalSeconds = 3600;

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private readonly IOptionsMonitor<ResourceManagerOptions> _options;
    private ResourceOrchestratorSelection _selection;

    public ResourceOrchestratorSelectionStore(
        IHostEnvironment environment,
        IOptionsMonitor<ResourceManagerOptions> options)
    {
        _options = options;
        _settingsPath = Path.GetFullPath(
            "Data/orchestration-settings.json",
            environment.ContentRootPath);
        _selection = Load();
    }

    public ResourceOrchestratorSelection Get()
    {
        lock (_gate)
        {
            return _selection;
        }
    }

    public ResourceHealthCheckIntervalSettings GetHealthCheckIntervalSettings()
    {
        var configuredInterval = _options.CurrentValue.HealthCheckIntervalSeconds;
        if (configuredInterval is not null)
        {
            return new ResourceHealthCheckIntervalSettings(
                NormalizeHealthCheckInterval(configuredInterval.Value),
                true);
        }

        return new ResourceHealthCheckIntervalSettings(
            Get().HealthCheckIntervalSeconds,
            false);
    }

    public void Select(
        string orchestratorId,
        string? preferredContainerEngineId = null,
        int healthCheckIntervalSeconds = DefaultHealthCheckIntervalSeconds)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorId);

        lock (_gate)
        {
            _selection = new ResourceOrchestratorSelection(
                orchestratorId.Trim(),
                NormalizeOptional(preferredContainerEngineId),
                NormalizeHealthCheckInterval(healthCheckIntervalSeconds),
                DateTimeOffset.UtcNow);
            Persist();
        }
    }

    private ResourceOrchestratorSelection Load()
    {
        if (!File.Exists(_settingsPath))
        {
            return ResourceOrchestratorSelection.Default;
        }

        var json = File.ReadAllText(_settingsPath);
        return NormalizeSelection(
            JsonSerializer.Deserialize<ResourceOrchestratorSelection>(json, SerializerOptions)
            ?? ResourceOrchestratorSelection.Default);
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_selection, SerializerOptions));
    }

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static int NormalizeHealthCheckInterval(int value) =>
        Math.Clamp(
            value,
            MinimumHealthCheckIntervalSeconds,
            MaximumHealthCheckIntervalSeconds);

    private static ResourceOrchestratorSelection NormalizeSelection(ResourceOrchestratorSelection selection) =>
        selection with
        {
            OrchestratorId = string.IsNullOrWhiteSpace(selection.OrchestratorId)
                ? ResourceOrchestratorSelection.Default.OrchestratorId
                : selection.OrchestratorId.Trim(),
            PreferredContainerEngineId = NormalizeOptional(selection.PreferredContainerEngineId),
            HealthCheckIntervalSeconds = NormalizeHealthCheckInterval(
                selection.HealthCheckIntervalSeconds == 0
                    ? DefaultHealthCheckIntervalSeconds
                    : selection.HealthCheckIntervalSeconds)
        };
}

public sealed record ResourceOrchestratorSelection(
    string OrchestratorId,
    string? PreferredContainerEngineId,
    int HealthCheckIntervalSeconds,
    DateTimeOffset UpdatedAt)
{
    public static ResourceOrchestratorSelection Default { get; } = new(
        "default",
        null,
        ResourceOrchestratorSelectionStore.DefaultHealthCheckIntervalSeconds,
        DateTimeOffset.MinValue);
}

public sealed record ResourceHealthCheckIntervalSettings(
    int Seconds,
    bool IsConfigured);
