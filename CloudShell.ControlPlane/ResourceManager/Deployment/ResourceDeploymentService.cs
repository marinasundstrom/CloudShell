using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Orchestration;

namespace CloudShell.ControlPlane.ResourceManager.Deployment;

public sealed class ResourceDeploymentService(
    IEnumerable<IResourceOrchestrator> orchestrators,
    IEnumerable<IResourceOrchestratorDeploymentApplier> deploymentAppliers,
    IResourceManagerStore resourceManager,
    IResourceRegistrationStore registrations,
    ResourceOrchestratorSelectionStore selectionStore,
    IResourceEventSink? resourceEvents = null,
    IResourceOrchestratorDeploymentStore? deploymentStore = null)
{
    private readonly IReadOnlyList<IResourceOrchestrator> orchestrators = orchestrators.ToArray();
    private readonly IReadOnlyList<IResourceOrchestratorDeploymentApplier> deploymentAppliers =
        deploymentAppliers.ToArray();

    public async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        Resource resource,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default,
        string? triggeredBy = null,
        string? cause = null)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(deployment);

        if (!string.Equals(resource.Id, deployment.SourceResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Deployment '{deployment.Id}' belongs to resource '{deployment.SourceResourceId}', not '{resource.Id}'."));
        }

        var context = CreateContext(resource, triggeredBy, cause);
        var applier = SelectDeploymentApplier(context, deployment);
        var applyingDeployment = deployment with { Status = ResourceOrchestratorDeploymentStatus.Applying };
        deploymentStore?.RecordApplying(
            applyingDeployment,
            DateTimeOffset.UtcNow,
            triggeredBy,
            cause);
        AppendResourceActionEvent(
            resource,
            ResourceEventTypes.Events.Deployment.Applying,
            $"Applying deployment '{deployment.Id}' for revision '{deployment.RevisionId}'.{FormatCause(cause)}",
            triggeredBy);

        try
        {
            var result = await applier.ApplyDeploymentAsync(
                context,
                applyingDeployment,
                cancellationToken);
            deploymentStore?.RecordApplied(
                result.Deployment,
                result.Revision,
                DateTimeOffset.UtcNow,
                result.ProcedureResult.Message,
                triggeredBy,
                cause);
            AppendResourceActionEvent(
                resource,
                ResourceEventTypes.Events.Deployment.Applied,
                $"Applied deployment '{deployment.Id}' for revision '{result.Deployment.RevisionId}'. Result: {result.ProcedureResult.Message}",
                triggeredBy);
            return result;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            deploymentStore?.RecordFailed(
                deployment,
                DateTimeOffset.UtcNow,
                exception.Message,
                triggeredBy,
                cause);
            AppendResourceActionEvent(
                resource,
                ResourceEventTypes.Events.Deployment.Failed,
                $"Failed deployment '{deployment.Id}' for revision '{deployment.RevisionId}'. Reason: {exception.Message}",
                triggeredBy,
                ResourceSignalSeverity.Error);
            throw;
        }
    }

    private ResourceOrchestrationContext CreateContext(
        Resource resource,
        string? triggeredBy = null,
        string? cause = null)
    {
        var registration = GetRegistrationForResourceOrAncestor(resource);
        return new ResourceOrchestrationContext(
            resource,
            registration,
            resourceManager.GetGroupForResource(resource.Id),
            resourceManager,
            registrations,
            selectionStore.Get().PreferredContainerHostId,
            triggeredBy,
            cause,
            resourceEvents);
    }

    private IResourceOrchestratorDeploymentApplier SelectDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment)
    {
        if (!string.IsNullOrWhiteSpace(deployment.OrchestratorId))
        {
            var explicitOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, deployment.OrchestratorId, StringComparison.OrdinalIgnoreCase));
            if (explicitOrchestrator is null)
            {
                throw new ControlPlaneException(
                    ControlPlaneError.InvalidRequest(
                        $"Orchestrator '{deployment.OrchestratorId}' is not registered for deployment '{deployment.Id}'."));
            }

            if (explicitOrchestrator is IResourceOrchestratorDeploymentApplier explicitApplier &&
                explicitApplier.CanApplyDeployment(context, deployment))
            {
                return explicitApplier;
            }

            var defaultApplier = SelectStandaloneDeploymentApplier(context, deployment);
            if (defaultApplier is not null)
            {
                return defaultApplier;
            }

            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Orchestrator '{deployment.OrchestratorId}' cannot apply deployment '{deployment.Id}' for resource '{context.Resource.Name}', and no default deployment service can apply it."));
        }

        return SelectPreferredDeploymentApplier(context, deployment)
            ?? throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"No deployment service can apply deployment '{deployment.Id}' for resource '{context.Resource.Name}'."));
    }

    private IResourceOrchestratorDeploymentApplier? SelectPreferredDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment)
    {
        var selectedId = selectionStore.Get().OrchestratorId;
        if (!string.Equals(selectedId, "default", StringComparison.OrdinalIgnoreCase))
        {
            var selected = orchestrators.FirstOrDefault(orchestrator =>
                string.Equals(orchestrator.Id, selectedId, StringComparison.OrdinalIgnoreCase) &&
                orchestrator is IResourceOrchestratorDeploymentApplier applier &&
                applier.CanApplyDeployment(context, deployment));
            if (selected is IResourceOrchestratorDeploymentApplier selectedApplier)
            {
                return selectedApplier;
            }
        }

        var defaultOrchestrator = orchestrators.FirstOrDefault(orchestrator =>
            string.Equals(orchestrator.Id, "default", StringComparison.OrdinalIgnoreCase) &&
            orchestrator is IResourceOrchestratorDeploymentApplier applier &&
            applier.CanApplyDeployment(context, deployment));
        if (defaultOrchestrator is IResourceOrchestratorDeploymentApplier defaultNativeApplier)
        {
            return defaultNativeApplier;
        }

        return SelectStandaloneDeploymentApplier(context, deployment);
    }

    private IResourceOrchestratorDeploymentApplier? SelectStandaloneDeploymentApplier(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment) =>
        deploymentAppliers.FirstOrDefault(applier => applier.CanApplyDeployment(context, deployment));

    private ResourceRegistration? GetRegistrationForResourceOrAncestor(Resource resource)
    {
        var current = resource;
        while (true)
        {
            var registration = registrations.GetRegistration(current.Id);
            if (registration is not null)
            {
                return registration;
            }

            if (string.IsNullOrWhiteSpace(current.ParentResourceId))
            {
                return null;
            }

            var parent = resourceManager.GetResource(current.ParentResourceId);
            if (parent is null)
            {
                return null;
            }

            current = parent;
        }
    }

    private void AppendResourceActionEvent(
        Resource resource,
        string eventType,
        string message,
        string? triggeredBy,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info) =>
        resourceEvents?.Append(new ResourceEvent(
            resource.Id,
            eventType,
            message,
            DateTimeOffset.UtcNow,
            triggeredBy,
            severity));

    private static string FormatCause(string? cause) =>
        string.IsNullOrWhiteSpace(cause)
            ? string.Empty
            : $" Cause: {cause.Trim().TrimEnd('.')}.";
}
