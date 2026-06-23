using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentTearDownPlanner
{
    public IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> PlanTearDown(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        ApplicationContainerDeployment? appDeployment,
        ApplicationContainerRevisionHistoryEntry? basedOnRevision,
        ResourceOrchestratorService defaultService,
        Func<ResourceOrchestratorReplicaGroup, bool> hasVisibleLegacyReplicaGroup)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(applyResult);
        ArgumentNullException.ThrowIfNull(defaultService);
        ArgumentNullException.ThrowIfNull(hasVisibleLegacyReplicaGroup);

        var basedOnRevisionId = NormalizeNullable(appDeployment?.BasedOnRevisionId);
        if (basedOnRevisionId is null ||
            string.Equals(basedOnRevisionId, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
        {
            return DescribeLegacyStableReplicaGroupTearDown(
                application,
                applyResult,
                defaultService,
                hasVisibleLegacyReplicaGroup);
        }

        var sourceService = CreateSupersededService(
            application,
            basedOnRevisionId,
            basedOnRevision,
            defaultService);
        var sourceReplicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(sourceService);
        return
        [
            new ResourceOrchestratorReplicaGroupTearDownRequest(
                sourceService,
                sourceReplicaGroup,
                $"Image deployment retired superseded container app revision '{basedOnRevisionId}'.")
        ];
    }

    private static IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> DescribeLegacyStableReplicaGroupTearDown(
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        ResourceOrchestratorService defaultService,
        Func<ResourceOrchestratorReplicaGroup, bool> hasVisibleLegacyReplicaGroup)
    {
        var appliedReplicaGroup = applyResult.Revision.ReplicaGroup;
        if (appliedReplicaGroup?.RuntimeRevisionId is null)
        {
            return [];
        }

        var sourceReplicas = application.ContainerRevisions
            .FirstOrDefault(revision =>
                string.Equals(revision.Id, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
            ?.RequestedReplicas ?? application.Replicas;
        var sourceService = defaultService with
        {
            Workload = defaultService.Workload with
            {
                Replicas = Math.Max(1, sourceReplicas),
                ReplicasEnabled = Math.Max(1, sourceReplicas) > 1
            },
            RuntimeRevisionId = null
        };
        var sourceReplicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(sourceService);
        if (string.Equals(sourceReplicaGroup.Id, appliedReplicaGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!hasVisibleLegacyReplicaGroup(sourceReplicaGroup))
        {
            return [];
        }

        return
        [
            new ResourceOrchestratorReplicaGroupTearDownRequest(
                sourceService,
                sourceReplicaGroup,
                $"Deployment retired legacy stable container app replica group for revision '{applyResult.Deployment.RevisionId}'.")
        ];
    }

    private static ResourceOrchestratorService CreateSupersededService(
        ApplicationResourceDefinition application,
        string basedOnRevisionId,
        ApplicationContainerRevisionHistoryEntry? basedOnRevision,
        ResourceOrchestratorService defaultService)
    {
        var sourceReplicas = Math.Max(
            1,
            basedOnRevision?.RequestedReplicas ??
            application.ContainerRevisions.FirstOrDefault(revision =>
                string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase))
                ?.RequestedReplicas ??
            application.Replicas);
        return defaultService with
        {
            Workload = defaultService.Workload with
            {
                Replicas = sourceReplicas,
                ReplicasEnabled = sourceReplicas > 1
            },
            RuntimeRevisionId = string.IsNullOrWhiteSpace(basedOnRevision?.DeploymentId)
                ? null
                : basedOnRevisionId
        };
    }

    private static string? NormalizeNullable(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
