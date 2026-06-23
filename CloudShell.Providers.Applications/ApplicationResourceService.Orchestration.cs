using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
namespace CloudShell.Providers.Applications;

public sealed partial class ApplicationResourceService
{
    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        ContainerOrchestratorDeploymentFactory.CreateService(
            application,
            CreateWorkloadConfiguration(application));

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
        var revision = GetEffectiveContainerRevision(application);
        return ContainerOrchestratorDeploymentFactory.CreateDeployment(
            application,
            state,
            CreateWorkloadConfiguration(application),
            runtimeRevisionScoped &&
                ShouldUseRevisionScopedRuntimeInstances(application, revision));
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
        var basedOnRevision = string.IsNullOrWhiteSpace(appDeployment?.BasedOnRevisionId)
            ? null
            : containerDeployments
                .ListRevisions(application.Id)
                .FirstOrDefault(revision =>
                    string.Equals(revision.Id, appDeployment.BasedOnRevisionId, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(ContainerDeploymentTearDownPlanner.PlanTearDown(
            application,
            applyResult,
            appDeployment,
            basedOnRevision,
            CreateDefaultContainerOrchestratorService(application),
            replicaGroup => HasVisibleLegacyReplicaGroup(context, replicaGroup)));
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
        var plan = ContainerDeploymentFailurePlanner.PlanApplyFailure(
            application,
            deployment.Id,
            deployment.RevisionId,
            appDeployment,
            NormalizeDefinition);

        containerDeployments.RecordDeploymentFailed(
            application.Id,
            plan.DeploymentId,
            plan.RevisionId,
            plan.BasedOnRevisionId);

        if (plan.RestoredDefinition is not null)
        {
            store.Save(plan.RestoredDefinition);
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

    private static ResourceOrchestratorReplicaGroup CreateDefaultContainerReplicaGroup(
        ResourceOrchestratorService service) =>
        ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

}
