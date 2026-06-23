using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Orchestration;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public sealed class DefaultResourceDeploymentService(
    IResourceOrchestratorDeploymentStore? deploymentStore = null) :
    IResourceOrchestratorDeploymentApplier
{
    public bool CanApplyDeployment(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment) =>
        ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start) is not null;

    public async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default)
    {
        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Start)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        var resourceContext = ResourceOrchestratorProviderResolver.CreateProcedureContext(context);
        var service = deployment.Spec.Service with
        {
            RuntimeRevisionId = deployment.RevisionId
        };
        var replicaGroup = ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);
        try
        {
            await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
                provider,
                resourceContext,
                service,
                ResourceAction.Start,
                cancellationToken,
                deployment,
                replicaGroup);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            await RollBackFailedDeploymentAsync(
                provider,
                resourceContext,
                service,
                replicaGroup,
                deployment,
                exception,
                cancellationToken);
            throw;
        }

        var applied = deployment with { Status = ResourceOrchestratorDeploymentStatus.Active };
        var revisionCreatedAt = DateTimeOffset.UtcNow;
        var revision = deploymentStore?.CreateRevision(
            applied,
            revisionCreatedAt,
            ResourceOrchestratorRevisionStatus.Active,
            replicaGroup,
            context.TriggeredBy) ??
            new ResourceOrchestratorRevision(
                CreateFallbackEnvironmentRevisionId(applied, revisionCreatedAt),
                applied.Id,
                applied.SourceResourceId,
                applied.ServiceId,
                RevisionNumber: 1,
                revisionCreatedAt,
                ResourceOrchestratorRevisionStatus.Active,
                replicaGroup,
                applied.BasedOnRevisionId,
                context.TriggeredBy);
        return new ResourceOrchestratorDeploymentApplyResult(
            applied,
            revision,
            ResourceProcedureResult.Completed(
                $"Applied deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}'."));
    }

    private static async Task RollBackFailedDeploymentAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceOrchestratorDeployment deployment,
        Exception applyException,
        CancellationToken cancellationToken)
    {
        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RollingBack,
            $"Rolling back deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}' after apply failed. Reason: {applyException.Message}",
            ResourceSignalSeverity.Warning);

        try
        {
            await ResourceOrchestratorServiceExecutor.ExecuteReplicaGroupAsync(
                provider,
                resourceContext,
                service,
                replicaGroup,
                ResourceAction.Stop,
                deployment: null,
                cancellationToken);
        }
        catch (Exception rollbackException) when (rollbackException is not OperationCanceledException)
        {
            ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.RollbackFailed,
                $"Rollback failed for deployment '{deployment.Id}' runtime revision '{deployment.RevisionId}'. Reason: {rollbackException.Message}",
                ResourceSignalSeverity.Error);
            return;
        }

        ResourceOrchestratorServiceExecutor.AppendDeploymentEvent(
            resourceContext,
            ResourceEventTypes.Events.Deployment.RolledBack,
            $"Rolled back deployment '{deployment.Id}' for runtime revision '{deployment.RevisionId}' by tearing down replica group '{replicaGroup.Id}'.",
            ResourceSignalSeverity.Warning);
    }

    private static ResourceOrchestratorEnvironmentRevisionId CreateFallbackEnvironmentRevisionId(
        ResourceOrchestratorDeployment deployment,
        DateTimeOffset createdAt) =>
        new(
            string.Create(
                CultureInfo.InvariantCulture,
                $"env-{deployment.Id}-{createdAt:yyyyMMddHHmmssfff}"));
}
