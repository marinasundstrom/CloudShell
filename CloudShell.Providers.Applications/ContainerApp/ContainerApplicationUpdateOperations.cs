using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.Providers.Applications;

internal sealed class ContainerApplicationUpdateOperations(
    ApplicationResourceStore store,
    ApplicationContainerDeploymentStore containerDeployments,
    IApplicationResourceRunningStateOperations runningState,
    ApplicationResourceDefinitionNormalizer definitionNormalizer,
    IResourceEventSink? resourceEvents = null) :
    IContainerApplicationUpdateOperations
{
    private static readonly ContainerApplicationDeploymentPlanner DeploymentPlanner = new();
    private static readonly ContainerApplicationScalingPlanner ScalingPlanner = new();

    public bool CanUpdateImage(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public bool CanUpdateReplicas(Resource resource) =>
        ApplicationResourceTypes.IsContainerApp(resource.EffectiveTypeId) &&
        store.GetApplication(resource.Id) is not null;

    public async Task<ResourceProcedureResult> UpdateImageAsync(
        ResourceProcedureContext context,
        string image,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default,
        int? requestedReplicas = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(image);
        if (requestedReplicas is < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedReplicas),
                requestedReplicas,
                "Requested replicas must be greater than or equal to 1.");
        }

        var application = GetContainerApplication(context.Resource.Id);
        var normalizedImage = image.Trim();
        var requestedReplicaCount = requestedReplicas ?? application.Replicas;
        if (string.Equals(application.ContainerImage, normalizedImage, StringComparison.Ordinal) &&
            requestedReplicaCount == application.Replicas)
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses image '{normalizedImage}'.");
        }

        var wasRunning = runningState.IsRunning(application.Id);
        var plan = DeploymentPlanner.PlanImageDeployment(
            application,
            normalizedImage,
            requestedReplicaCount,
            requestedReplicas.HasValue,
            triggeredBy,
            ApplicationContainerOrchestratorDeploymentFactory.CreateDeploymentId(application.Id),
            definitionNormalizer.Normalize);
        var updated = plan.Definition;
        store.Save(updated);
        containerDeployments.RecordDeployment(
            plan.Deployment,
            plan.Revision,
            plan.BasedOnRevision);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Deployment.ImageUpdated,
            $"Deployed container image '{normalizedImage}' from '{application.ContainerImage ?? "none"}' and produced revision '{updated.ContainerRevision}' with requested replicas '{FormatRequestedReplicas(requestedReplicas)}'.",
            DateTimeOffset.UtcNow,
            triggeredBy));

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
                $"Deployed {application.Name} image '{normalizedImage}' and produced revision '{updated.ContainerRevision}'.",
                application.Id,
                "The container app is running. Runtime reconciliation is required to cut over to this deployment.")
            : ResourceProcedureResult.Completed(
                $"Deployed {application.Name} image '{normalizedImage}' and produced revision '{updated.ContainerRevision}'.");
    }

    public async Task<ResourceProcedureResult> UpdateReplicasAsync(
        ResourceProcedureContext context,
        int replicas,
        bool restartIfRunning,
        string? triggeredBy = null,
        CancellationToken cancellationToken = default)
    {
        if (replicas < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(replicas), replicas, "Replicas must be greater than or equal to 1.");
        }

        var application = GetContainerApplication(context.Resource.Id);
        if (application.ReplicasEnabled && application.Replicas == replicas)
        {
            return ResourceProcedureResult.Completed(
                $"Container app '{application.Name}' already uses {replicas} replica{Pluralize(replicas)}.");
        }

        var wasRunning = runningState.IsRunning(application.Id);
        var plan = ScalingPlanner.PlanReplicaUpdate(
            application,
            replicas,
            definitionNormalizer.Normalize);
        var updated = plan.Definition;

        store.Save(updated);

        resourceEvents?.Append(new ResourceEvent(
            application.Id,
            ResourceEventTypes.Events.Deployment.ReplicasUpdated,
            $"Changed container app replicas from '{application.Replicas}' to '{updated.Replicas}'.",
            DateTimeOffset.UtcNow,
            triggeredBy));

        return wasRunning
            ? ResourceProcedureResult.CompletedWithRuntimeReconciliationRequired(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.",
                application.Id,
                "The container app is running. Runtime reconciliation is required to apply the replica count.")
            : ResourceProcedureResult.Completed(
                $"Updated {application.Name} to {updated.Replicas} replica{Pluralize(updated.Replicas)}.");
    }

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

    private static string FormatRequestedReplicas(int? requestedReplicas) =>
        requestedReplicas is { } value
            ? value.ToString(CultureInfo.InvariantCulture)
            : "unchanged";

    private static string Pluralize(int count) =>
        count == 1 ? string.Empty : "s";
}
