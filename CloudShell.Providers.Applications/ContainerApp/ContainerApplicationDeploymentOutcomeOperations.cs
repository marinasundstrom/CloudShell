using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationDeploymentOutcomeOperations(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    ApplicationResourceDefinitionNormalizer definitionNormalizer,
    ApplicationWorkloadConfigurationProvider workloadConfigurations,
    IResourceEventSink? resourceEvents = null) :
    IContainerApplicationDeploymentOutcomeOperations
{
    private static readonly ContainerApplicationDeploymentAppliedPlanner DeploymentAppliedPlanner = new();
    private static readonly ContainerApplicationDeploymentFailurePlanner DeploymentFailurePlanner = new();
    private static readonly ContainerApplicationDeploymentTearDownPlanner DeploymentTearDownPlanner = new();
    private static readonly ApplicationContainerOrchestratorDeploymentFactory OrchestratorDeploymentFactory = new();

    public bool CanDescribeDeploymentTearDown(Resource resource) =>
        CanHandleContainerApplicationDeployment(resource);

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

        return Task.FromResult(DeploymentTearDownPlanner.PlanTearDown(
            application,
            applyResult,
            appDeployment,
            basedOnRevision,
            CreateDefaultContainerOrchestratorService(application),
            replicaGroup => HasVisibleLegacyReplicaGroup(context, replicaGroup)));
    }

    public bool CanHandleDeploymentApplied(Resource resource) =>
        CanHandleContainerApplicationDeployment(resource);

    public Task HandleDeploymentAppliedAsync(
        ResourceProcedureContext context,
        ResourceOrchestratorDeploymentApplyResult applyResult,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var application = GetContainerApplication(context.Resource.Id);
        var updated = DeploymentAppliedPlanner.PlanAppliedDeployment(
            application,
            applyResult,
            definitionNormalizer.Normalize);
        if (updated is null)
        {
            return Task.CompletedTask;
        }

        store.Save(updated);

        return Task.CompletedTask;
    }

    public bool CanHandleDeploymentApplyFailed(Resource resource) =>
        CanHandleContainerApplicationDeployment(resource);

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
        var plan = DeploymentFailurePlanner.PlanApplyFailure(
            application,
            deployment.Id,
            deployment.RevisionId,
            appDeployment,
            definitionNormalizer.Normalize);

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

    private bool CanHandleContainerApplicationDeployment(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    private ApplicationResourceDefinition GetContainerApplication(string resourceId)
    {
        var application = store.GetApplication(resourceId)
            ?? throw new InvalidOperationException(
                $"Container app resource '{resourceId}' is not configured.");
        if (!ApplicationResourceTypes.IsContainerApp(application.ResourceType))
        {
            throw new InvalidOperationException(
                $"Resource '{resourceId}' is not a container app.");
        }

        return application;
    }

    private ResourceOrchestratorService CreateDefaultContainerOrchestratorService(
        ApplicationResourceDefinition application) =>
        OrchestratorDeploymentFactory.CreateService(
            application,
            workloadConfigurations.Create(application));

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
}
