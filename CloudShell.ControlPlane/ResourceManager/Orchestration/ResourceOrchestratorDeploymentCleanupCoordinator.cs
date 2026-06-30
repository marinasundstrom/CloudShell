using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class ResourceOrchestratorDeploymentCleanupCoordinator(
    IResourceEventSink? resourceEvents = null) : IResourceOrchestratorDeploymentCleanupCoordinator
{
    public async Task<ResourceProcedureResult> RunPostApplyCleanupAsync(
        Resource resource,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        ResourceProcedureResult result,
        string? triggeredBy,
        Func<ResourceOrchestratorDeploymentApplyResult, CancellationToken, Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>>? describeTearDownsAsync,
        Func<ResourceOrchestratorReplicaGroupTearDownRequest, ResourceOrchestratorReplicaGroup, string, CancellationToken, Task<ResourceProcedureResult>> tearDownReplicaGroupAsync,
        Func<ResourceProcedureResult, ResourceProcedureResult, ResourceProcedureResult>? mergeResults = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(applyResult);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(tearDownReplicaGroupAsync);

        var tearDowns = applyResult.ReplicaGroupsToTearDown;
        if (tearDowns.Count == 0 && describeTearDownsAsync is not null)
        {
            tearDowns = await describeTearDownsAsync(applyResult, cancellationToken);
        }

        if (tearDowns.Count == 0)
        {
            return result;
        }

        var merge = mergeResults ?? MergeProcedureResults;
        var mergedResult = result;
        foreach (var tearDown in tearDowns)
        {
            var replicaGroup = tearDown.ReplicaGroup ??
                ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(tearDown.Service);
            var reason = tearDown.Reason ?? "Deployment retired superseded replica group.";
            AppendDeploymentEvent(
                resource,
                ResourceEventTypes.Events.Deployment.CleanupRunning,
                $"Cleaning up superseded replica group '{replicaGroup.Id}' for deployment '{applyResult.Deployment.Id}'. Reason: {reason}",
                triggeredBy,
                ResourceSignalSeverity.Info);
            try
            {
                var tearDownResult = await tearDownReplicaGroupAsync(
                    tearDown,
                    replicaGroup,
                    reason,
                    cancellationToken);
                AppendDeploymentEvent(
                    resource,
                    ResourceEventTypes.Events.Deployment.CleanupCompleted,
                    $"Cleaned up superseded replica group '{replicaGroup.Id}' for deployment '{applyResult.Deployment.Id}'. Result: {tearDownResult.Message}",
                    triggeredBy,
                    ResourceSignalSeverity.Info);
                mergedResult = merge(mergedResult, tearDownResult);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                var warning =
                    $"Post-apply cleanup for deployment '{applyResult.Deployment.Id}' could not tear down replica group '{replicaGroup.Id}'. Reason: {exception.Message}";
                AppendDeploymentEvent(
                    resource,
                    ResourceEventTypes.Events.Deployment.CleanupWarning,
                    warning,
                    triggeredBy,
                    ResourceSignalSeverity.Warning);
                mergedResult = AddProcedureWarning(mergedResult, warning);
            }
        }

        return mergedResult;
    }

    private static ResourceProcedureResult MergeProcedureResults(
        ResourceProcedureResult result,
        ResourceProcedureResult tearDownResult) =>
        ResourceProcedureResult.Combine(
            [result, tearDownResult],
            result.Message);

    private static ResourceProcedureResult AddProcedureWarning(
        ResourceProcedureResult result,
        string warning) =>
        result with
        {
            Signals = result.Signals
                .Append(ResourceProcedureSignal.Warning(warning))
                .ToArray()
        };

    private void AppendDeploymentEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity) =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));
}
