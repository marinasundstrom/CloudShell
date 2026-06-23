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
            [ResourceAttributeNames.DeploymentDesiredReplicas] =
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

    public async Task CompleteOrchestratorDeploymentAsync(
        ResourceOrchestratorDeploymentProcedureContext context,
        CancellationToken cancellationToken = default)
    {
        var application = GetContainerApplication(context.ResourceContext.Resource.Id);
        var appDeployment = containerDeployments
            .List(application.Id)
            .FirstOrDefault(deployment =>
                string.Equals(deployment.OrchestratorDeploymentId, context.Deployment.Id, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(deployment.RevisionId, context.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase));
        var sourceRevisionId = NormalizeNullable(appDeployment?.SourceRevisionId);
        if (sourceRevisionId is null ||
            string.Equals(sourceRevisionId, context.Deployment.RevisionId, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourceRevision = containerDeployments
            .ListRevisions(application.Id)
            .FirstOrDefault(revision =>
                string.Equals(revision.Id, sourceRevisionId, StringComparison.OrdinalIgnoreCase));
        var sourceService = CreateSupersededContainerOrchestratorService(
            application,
            sourceRevisionId,
            sourceRevision);
        var engine = await ResolveRequiredContainerHostAsync(
            application,
            context.ResourceContext.ResourceManager,
            context.ResourceContext.PreferredContainerHostId,
            ContainerHostCapabilityIds.ContainerImage,
            cancellationToken);
        var log = GetProcessLog(application.Id);
        context.ResourceContext.AppendProviderEvent(
            Id,
            "application.container.revision.retiring",
            $"Application provider is retiring superseded container app revision '{sourceRevisionId}' for '{application.Name}'.");

        var sourceReplicaGroup = CreateDefaultContainerReplicaGroup(sourceService);
        foreach (var instance in sourceReplicaGroup.Instances
                     .OrderByDescending(instance => instance.ReplicaOrdinal))
        {
            await StopContainerApplicationInstanceAsync(
                application,
                engine,
                log,
                instance,
                cancellationToken,
                context.ResourceContext);
        }

        context.ResourceContext.AppendProviderEvent(
            Id,
            "application.container.revision.retired",
            $"Application provider retired superseded container app revision '{sourceRevisionId}' for '{application.Name}'.");
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
