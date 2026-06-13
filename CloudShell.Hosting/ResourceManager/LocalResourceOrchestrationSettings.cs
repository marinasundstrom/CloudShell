using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed class LocalResourceOrchestrationSettings : IResourceOrchestrationSettings
{
    private readonly object syncRoot = new();
    private ResourceOrchestratorSelection selection = ResourceOrchestratorSelection.Default;

    public ResourceOrchestratorSelection Get()
    {
        lock (syncRoot)
        {
            return selection;
        }
    }

    public ResourceHealthCheckIntervalSettings GetHealthCheckIntervalSettings()
    {
        lock (syncRoot)
        {
            return new ResourceHealthCheckIntervalSettings(
                selection.HealthCheckIntervalSeconds,
                selection.UpdatedAt != DateTimeOffset.MinValue);
        }
    }

    public void Select(
        string orchestratorId,
        string? preferredContainerHostId = null,
        int healthCheckIntervalSeconds = ResourceOrchestratorSelectionDefaults.DefaultHealthCheckIntervalSeconds)
    {
        selection = new ResourceOrchestratorSelection(
            string.IsNullOrWhiteSpace(orchestratorId)
                ? ResourceOrchestratorSelection.Default.OrchestratorId
                : orchestratorId,
            preferredContainerHostId,
            ResourceOrchestratorSelectionDefaults.NormalizeHealthCheckInterval(healthCheckIntervalSeconds),
            DateTimeOffset.UtcNow);
    }
}
