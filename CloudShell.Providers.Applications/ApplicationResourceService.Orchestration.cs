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
        containerDeployments.ListRevisions(application.Id).Any(entry =>
            string.Equals(entry.Id, revision, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(entry.Status, ApplicationContainerRevisionStatuses.Active, StringComparison.OrdinalIgnoreCase));

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
        var sourceRevisionId = NormalizeNullable(appDeployment?.SourceRevisionId);
        if (sourceRevisionId is null ||
            string.Equals(sourceRevisionId, applyResult.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>([]);
        }

        var sourceRevision = containerDeployments
            .ListRevisions(application.Id)
            .FirstOrDefault(revision =>
                string.Equals(revision.Id, sourceRevisionId, StringComparison.OrdinalIgnoreCase));
        var sourceService = CreateSupersededContainerOrchestratorService(
            application,
            sourceRevisionId,
            sourceRevision);
        var sourceReplicaGroup = CreateDefaultContainerReplicaGroup(sourceService);
        return Task.FromResult<IReadOnlyList<ResourceOrchestratorReplicaGroupTearDownRequest>>(
            [
                new ResourceOrchestratorReplicaGroupTearDownRequest(
                    sourceService,
                    sourceReplicaGroup,
                    $"Image deployment retired superseded container app revision '{sourceRevisionId}'.")
            ]);
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
        var sourceRevisionId =
            NormalizeNullable(appDeployment?.SourceRevisionId) ??
            application.ContainerRevisions
                .FirstOrDefault(revision =>
                    string.Equals(revision.Id, deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
                ?.SourceRevisionId;

        containerDeployments.RecordDeploymentFailed(
            application.Id,
            appDeployment?.Id ?? deployment.Id,
            deployment.RevisionId,
            sourceRevisionId);

        if (!string.IsNullOrWhiteSpace(sourceRevisionId))
        {
            var sourceRevision = application.ContainerRevisions.FirstOrDefault(revision =>
                string.Equals(revision.Id, sourceRevisionId, StringComparison.OrdinalIgnoreCase));
            if (sourceRevision is not null)
            {
                var restored = NormalizeDefinition(application with
                {
                    ContainerImage = sourceRevision.Image,
                    ContainerRevision = sourceRevision.Id,
                    Replicas = Math.Max(1, sourceRevision.RequestedReplicas),
                    ReplicasEnabled = sourceRevision.RequestedReplicas > 1,
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
        string sourceRevisionId,
        ApplicationContainerRevisionHistoryEntry? sourceRevision)
    {
        var sourceReplicas = Math.Max(
            1,
            sourceRevision?.RequestedReplicas ??
            application.ContainerRevisions.FirstOrDefault(revision =>
                string.Equals(revision.Id, sourceRevisionId, StringComparison.OrdinalIgnoreCase))
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
            RuntimeRevisionId = string.IsNullOrWhiteSpace(sourceRevision?.DeploymentId)
                ? null
                : sourceRevisionId
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
