using System.Text.Json;
using Microsoft.Extensions.Hosting;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class ResourceOrchestratorSelectionStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _settingsPath;
    private ResourceOrchestratorSelection _selection;

    public ResourceOrchestratorSelectionStore(IHostEnvironment environment)
    {
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

    public void Select(string orchestratorId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orchestratorId);

        lock (_gate)
        {
            _selection = new ResourceOrchestratorSelection(orchestratorId.Trim(), DateTimeOffset.UtcNow);
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
        return JsonSerializer.Deserialize<ResourceOrchestratorSelection>(json, SerializerOptions)
            ?? ResourceOrchestratorSelection.Default;
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(_selection, SerializerOptions));
    }
}

public sealed record ResourceOrchestratorSelection(
    string OrchestratorId,
    DateTimeOffset UpdatedAt)
{
    public static ResourceOrchestratorSelection Default { get; } = new("default", DateTimeOffset.MinValue);
}
