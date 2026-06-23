using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        new(
            application.Id,
            GetContainerServiceName(application.Id),
            CreateWorkloadConfiguration(application),
            Networks: [DefaultContainerNetworkName]);

    private ResourceOrchestratorService CreateActiveContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        CreateDefaultContainerOrchestratorDeployment(
            application,
            GetState(application.Id),
            runtimeRevisionScoped: true)
            .Spec
            .Service;

    private ResourceOrchestratorDeployment CreateDefaultContainerOrchestratorDeployment(
        ApplicationResourceDefinition application,
        ResourceState state,
        bool runtimeRevisionScoped = false)
    {
        var service = CreateDefaultContainerOrchestratorService(application);
        var revision = GetEffectiveContainerRevision(application);
        if (runtimeRevisionScoped &&
            ShouldUseRevisionScopedRuntimeInstances(application, revision))
        {
            service = service with
            {
                RuntimeRevisionId = revision
            };
        }

        var inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [ResourceAttributeNames.DeploymentRequestedReplicas] =
                service.Replicas.ToString(CultureInfo.InvariantCulture),
            [ResourceAttributeNames.ContainerRegistry] = GetEffectiveContainerRegistry(application)
        };

        AddIfNotEmpty(inputs, ResourceAttributeNames.ContainerImage, application.ContainerImage);

        return new ResourceOrchestratorDeployment(
            CreateDefaultContainerOrchestratorDeploymentId(application.Id),
            DefaultOrchestratorId,
            application.Id,
            service.Name,
            revision,
            new ResourceOrchestratorDeploymentSpec(service, revision, inputs),
            GetContainerOrchestratorDeploymentStatus(state));
    }

    private bool ShouldUseRevisionScopedRuntimeInstances(
        ApplicationResourceDefinition application,
        string revision) =>
        !string.IsNullOrWhiteSpace(revision) &&
        (!string.IsNullOrWhiteSpace(application.DeploymentEnvironmentRevisionId) ||
            containerDeployments.ListRevisions(application.Id).Any(entry =>
                string.Equals(entry.Id, revision, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(entry.Status, ApplicationContainerRevisionStatuses.Active, StringComparison.OrdinalIgnoreCase)));

    public Task<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>> DescribeDeploymentTearDownAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = GetContainerApplication(context.Resource.Id);
        var appDeployment = containerDeployments
            .List(application.Id)
            .FirstOrDefault(deployment =>
                string.Equals(deployment.OrchestratorDeploymentId, applyResult.Deployment.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(deployment.RevisionId, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase));
        var basedOnRevisionId = NormalizeNullable(appDeployment?.BasedOnRevisionId);
        if (basedOnRevisionId is null ||
            string.Equals(basedOnRevisionId, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(DescribeLegacyStableReplicaGroupTearDown(
                context,
                application,
                applyResult));
        }

        var basedOnRevision = containerDeployments
            .ListRevisions(application.Id)
            .FirstOrDefault(revision =>
                string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase));
        var sourceService = CreateSupersededContainerOrchestratorService(
            application,
            basedOnRevisionId,
            basedOnRevision);
        var sourceReplicaGroup = CreateDefaultContainerReplicaGroup(sourceService);
        return Task.FromResult<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>(
            [
                new ResourceOrchestratorReplicaGroupTearDownRequest(
                    sourceService,
                    sourceReplicaGroup,
                    $"Image deployment retired superseded container app revision '{basedOnRevisionId}'.")
            ]);
    }

    private IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest> DescribeLegacyStableReplicaGroupTearDown(
        ResourceProcedureContext context,
        ApplicationResourceDefinition application,
        ResourceOrchestratorDeploymentApplyResult applyResult)
    {
        var appliedReplicaGroup = applyResult.Revision.ReplicaGroup;
        if (appliedReplicaGroup?.RuntimeRevisionId is null)
        {
            return [];
        }

        var sourceService = CreateDefaultContainerOrchestratorService(application);
        var sourceReplicas = application.ContainerRevisions
            .FirstOrDefault(revision =>
                string.Equals(revision.Id, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
            ?.RequestedReplicas ?? application.Replicas;
        sourceService = sourceService with
        {
            Workload = sourceService.Workload with
            {
                Replicas = Math.Max(1, sourceReplicas),
                ReplicasEnabled = Math.Max(1, sourceReplicas) > 1
            },
            RuntimeRevisionId = null
        };
        var sourceReplicaGroup = CreateDefaultContainerReplicaGroup(sourceService);
        if (string.Equals(sourceReplicaGroup.Id, appliedReplicaGroup.Id, StringComparison.OrdinalIgnoreCase))
        {
            return [];
        }

        if (!HasVisibleLegacyReplicaGroup(context, sourceReplicaGroup))
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

    private static bool HasVisibleLegacyReplicaGroup(
        ResourceProcedureContext context,
        ResourceOrchestratorReplicaGroup replicaGroup)
    {
        if (context.ResourceManager is null)
        {
            return true;
        }

        var instanceNames = replicaGroup.Instances
            .Select(instance => instance.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return context.ResourceManager
            .GetResources()
            .Any(resource =>
                instanceNames.Contains(resource.Name) ||
                (resource.ResourceAttributes.TryGetValue(ResourceAttributeNames.RuntimeContainerName, out var containerName) &&
                    instanceNames.Contains(containerName)));
    }

    public Task HandleDeploymentAppliedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = GetContainerApplication(context.Resource.Id);
        var environmentRevisionId = applyResult.Revision.Id.ToString();
        if (string.IsNullOrWhiteSpace(environmentRevisionId))
        {
            return Task.CompletedTask;
        }

        store.Save(NormalizeDefinition(application with
        {
            DeploymentEnvironmentRevisionId = environmentRevisionId
        }));

        return Task.CompletedTask;
    }

    public Task HandleDeploymentApplyFailedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeployment deployment,
        Exception exception,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = GetContainerApplication(context.Resource.Id);
        var appDeployment = containerDeployments
            .List(application.Id)
            .FirstOrDefault(candidate =>
                string.Equals(candidate.OrchestratorDeploymentId, deployment.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(candidate.RevisionId, deployment.RevisionId, StringComparison.OrdinalIgnoreCase));
        var basedOnRevisionId =
            NormalizeNullable(appDeployment?.BasedOnRevisionId) ??
            application.ContainerRevisions
                .FirstOrDefault(revision =>
                    string.Equals(revision.Id, deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
                ?.BasedOnRevisionId;

        containerDeployments.RecordDeploymentFailed(
            application.Id,
            appDeployment?.Id ?? deployment.Id,
            deployment.RevisionId,
            basedOnRevisionId);

        if (!string.IsNullOrWhiteSpace(basedOnRevisionId))
        {
            var basedOnRevision = application.ContainerRevisions.FirstOrDefault(revision =>
                string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase));
            if (basedOnRevision is not null)
            {
                var restored = NormalizeDefinition(application with
                {
                    ContainerImage = basedOnRevision.Image,
                    ContainerRevision = basedOnRevision.Id,
                    Replicas = Math.Max(1, basedOnRevision.RequestedReplicas),
                    ReplicasEnabled = basedOnRevision.RequestedReplicas > 1,
                    ContainerRevisions = application.ContainerRevisions
                        .Where(revision => !string.Equals(
                            revision.Id,
                            deployment.RevisionId,
                            StringComparison.OrdinalIgnoreCase))
                        .ToArray()
                });
                store.Save(restored);
            }
        }

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Deployment.Failed,
            $"Deployment '{deployment.Id}' for revision '{deployment.RevisionId}' failed before the container app revision became active. Reason: {exception.Message}",
            DateTimeOffset.UtcNow,
            context.TriggeredBy,
            ResourceSignalSeverity.Error));

        return Task.CompletedTask;
    }

    private ResourceOrchestratorService CreateSupersededContainerOrchestratorService(
        ApplicationResourceDefinition application,
        string basedOnRevisionId,
        ApplicationContainerRevisionHistoryEntry? basedOnRevision)
    {
        var sourceReplicas = Math.Max(
            1,
            basedOnRevision?.RequestedReplicas ??
            application.ContainerRevisions.FirstOrDefault(revision =>
                string.Equals(revision.Id, basedOnRevisionId, StringComparison.OrdinalIgnoreCase))
                ?.RequestedReplicas ??
            application.Replicas);
        var service = CreateDefaultContainerOrchestratorService(application);
        return service with
        {
            Workload = service.Workload with
            {
                Replicas = sourceReplicas,
                ReplicasEnabled = sourceReplicas > 1
            },
            RuntimeRevisionId = string.IsNullOrWhiteSpace(basedOnRevision?.DeploymentId)
                ? null
                : basedOnRevisionId
        };
    }

    private static ResourceOrchestratorReplicaGroup CreateDefaultContainerReplicaGroup(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

    private static ResourceOrchestratorDeploymentStatus GetContainerOrchestratorDeploymentStatus(
        ResourceState state) =>
        state switch
        {
            ResourceState.Starting or ResourceState.Stopping => ResourceOrchestratorDeploymentStatus.Applying,
            ResourceState.Running => ResourceOrchestratorDeploymentStatus.Active,
            ResourceState.Degraded => ResourceOrchestratorDeploymentStatus.Failed,
            _ => ResourceOrchestratorDeploymentStatus.Pending
        };
}
