using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.Providers;

public sealed class LocalContainerApplicationProcessRuntimeResourceProvider(
    LocalContainerApplicationProcessRuntimeBridge bridge) : IResourceProvider
{
    public string Id => "resource-model.container-app.local-process-runtime.resources";

    public string DisplayName => "Container app local process runtime";

    public IReadOnlyList<CloudShell.Abstractions.ResourceManager.Resource> GetResources() =>
        bridge.GetRuntimeReplicaResources();
}

public sealed class LocalContainerApplicationProcessRuntimeLogProvider(
    LocalContainerApplicationProcessRuntimeBridge bridge) : ILogProvider
{
    public string Id => "resource-model.container-app.local-process-runtime.logs";

    public string DisplayName => "Container app local process runtime";

    public IReadOnlyList<LogSource> GetLogSources() =>
        bridge.GetLogSources();

    public bool CanOpenLogSource(LogSource source) =>
        GetLogSources()
            .Any(candidate => string.Equals(candidate.Id, source.Id, StringComparison.OrdinalIgnoreCase));

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default) =>
        bridge.ReadLogSourceAsync(logSourceId, maxEntries, before, cancellationToken);
}
