using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationRuntimeStateStore : IResourceVolumeMountMaterializationStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly object _gate = new();
    private readonly string _statePath;
    private List<ApplicationRuntimeState> _states;

    public ApplicationRuntimeStateStore(
        LocalProcessOptions options,
        IHostEnvironment environment)
    {
        _statePath = ResolvePath(options.RuntimeStatePath, environment.ContentRootPath);
        _states = LoadStates();
    }

    public ApplicationRuntimeState? Get(string applicationId)
    {
        lock (_gate)
        {
            return _states.FirstOrDefault(state =>
                string.Equals(state.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public void Save(ApplicationRuntimeState state)
    {
        lock (_gate)
        {
            var index = _states.FindIndex(item =>
                string.Equals(item.ApplicationId, state.ApplicationId, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                _states[index] = state;
            }
            else
            {
                _states.Add(state);
            }

            Persist();
        }
    }

    public void Remove(string applicationId)
    {
        lock (_gate)
        {
            _states.RemoveAll(state =>
                string.Equals(state.ApplicationId, applicationId, StringComparison.OrdinalIgnoreCase));
            Persist();
        }
    }

    public IReadOnlyList<ResourceVolumeMountMaterialization> GetVolumeMountMaterializations(
        string resourceId) =>
        Get(resourceId)?.RuntimeVolumeMounts ?? [];

    public void SaveVolumeMountMaterializations(
        string resourceId,
        IReadOnlyList<ResourceVolumeMountMaterialization> materializations)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);
        var now = DateTimeOffset.UtcNow;
        var state = Get(resourceId);
        Save(state is null
            ? new ApplicationRuntimeState(
                resourceId,
                null,
                null,
                now,
                VolumeMounts: materializations)
            : state with
            {
                LastObservedAt = now,
                VolumeMounts = materializations
            });
    }

    private List<ApplicationRuntimeState> LoadStates()
    {
        if (!File.Exists(_statePath))
        {
            return [];
        }

        var json = File.ReadAllText(_statePath);
        return JsonSerializer.Deserialize<List<ApplicationRuntimeState>>(json, SerializerOptions) ?? [];
    }

    private void Persist()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_statePath)!);
        File.WriteAllText(_statePath, JsonSerializer.Serialize(_states, SerializerOptions));
    }

    private static string ResolvePath(string path, string contentRootPath) =>
        Path.IsPathRooted(path)
            ? path
            : Path.GetFullPath(path, contentRootPath);
}
