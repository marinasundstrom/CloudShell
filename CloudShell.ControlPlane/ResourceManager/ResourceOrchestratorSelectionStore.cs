using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceOrchestratorSelectionStore : IResourceOrchestrationSettings
{
    public const int DefaultHealthCheckIntervalSeconds =
        ResourceOrchestratorSelectionDefaults.DefaultHealthCheckIntervalSeconds;
    public const int MinimumHealthCheckIntervalSeconds =
        ResourceOrchestratorSelectionDefaults.MinimumHealthCheckIntervalSeconds;
    public const int MaximumHealthCheckIntervalSeconds =
        ResourceOrchestratorSelectionDefaults.MaximumHealthCheckIntervalSeconds;

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

    public ResourceDependencyStartFailureSettings GetDependencyStartFailureSettings()
    {
        var configuredBehavior = _options.CurrentValue.DependencyStartFailureBehavior;
        if (configuredBehavior is not null &&
            Enum.IsDefined(configuredBehavior.Value))
        {
            return new ResourceDependencyStartFailureSettings(
                configuredBehavior.Value,
                true);
        }

        return new ResourceDependencyStartFailureSettings(
            Get().DependencyStartFailureBehavior,
            false);
    }

    public void Select(
        string orchestratorId,
        string? preferredContainerHostId = null,
        int healthCheckIntervalSeconds = DefaultHealthCheckIntervalSeconds,
        DependencyStartFailureBehavior dependencyStartFailureBehavior = DependencyStartFailureBehavior.FailAction)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorId);

        lock (_gate)
        {
            _selection = new ResourceOrchestratorSelection(
                orchestratorId.Trim(),
                NormalizeOptional(preferredContainerHostId),
                NormalizeHealthCheckInterval(healthCheckIntervalSeconds),
                DateTimeOffset.UtcNow,
                dependencyStartFailureBehavior);
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
        ResourceOrchestratorSelectionDefaults.NormalizeHealthCheckInterval(value);

    private static ResourceOrchestratorSelection NormalizeSelection(ResourceOrchestratorSelection selection) =>
        selection with
        {
            OrchestratorId = string.IsNullOrWhiteSpace(selection.OrchestratorId)
                ? ResourceOrchestratorSelection.Default.OrchestratorId
                : selection.OrchestratorId.Trim(),
            PreferredContainerHostId = NormalizeOptional(selection.PreferredContainerHostId),
            HealthCheckIntervalSeconds = NormalizeHealthCheckInterval(
                selection.HealthCheckIntervalSeconds == 0
                    ? DefaultHealthCheckIntervalSeconds
                    : selection.HealthCheckIntervalSeconds),
            DependencyStartFailureBehavior = Enum.IsDefined(selection.DependencyStartFailureBehavior)
                ? selection.DependencyStartFailureBehavior
                : DependencyStartFailureBehavior.FailAction
        };
}
