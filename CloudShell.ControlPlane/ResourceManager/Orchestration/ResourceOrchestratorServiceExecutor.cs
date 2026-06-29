using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using System.Globalization;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

internal static class ResourceOrchestratorServiceExecutor
{
    public static async Task ExecuteServiceActionAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceAction action,
        CancellationToken cancellationToken,
        ResourceOrchestratorDeployment? deployment = null,
        ResourceOrchestratorReplicaGroup? replicaGroup = null,
        IReadOnlyList<ResourceOrchestratorServiceRoutingBindingDefinition>? routingBindings = null)
    {
        if (deployment is not null)
        {
            AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.ServiceReconciling,
                $"Reconciling orchestrator service '{deployment.ServiceId}' for deployment '{deployment.Id}'.");
        }

        replicaGroup ??= ResourceOrchestratorReplicaGroups.CreateDefaultReplicaGroup(service);

        await provider.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(
                resourceContext,
                service,
                replicaGroup,
                routingBindings),
            action,
            cancellationToken);

        if (deployment is not null)
        {
            AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.ServiceReconciled,
                $"Reconciled orchestrator service '{deployment.ServiceId}' for deployment '{deployment.Id}'.");
        }

        await ExecuteReplicaGroupAsync(
            provider,
            resourceContext,
            service,
            replicaGroup,
            action,
            deployment,
            cancellationToken);

        if (deployment is not null &&
            service.ServicePorts.Count > 0)
        {
            AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.RoutingUpdating,
                $"Updating routing for orchestrator service '{deployment.ServiceId}' to revision '{deployment.RevisionId}' for deployment '{deployment.Id}'.");
            await provider.ReconcileOrchestratorServiceRoutingAsync(
                new ResourceOrchestratorServiceProcedureContext(
                    resourceContext,
                    service,
                    replicaGroup,
                    routingBindings),
                cancellationToken);
            AppendDeploymentEvent(
                resourceContext,
                ResourceEventTypes.Events.Deployment.RoutingUpdated,
                $"Updated routing for orchestrator service '{deployment.ServiceId}' to revision '{deployment.RevisionId}' for deployment '{deployment.Id}'.");
        }
    }

    public static async Task ExecuteReplicaGroupAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        ResourceAction action,
        ResourceOrchestratorDeployment? deployment,
        CancellationToken cancellationToken)
    {
        foreach (var instance in replicaGroup.Instances)
        {
            var replicaPosition = FormatReplicaPosition(instance);
            if (deployment is not null)
            {
                AppendDeploymentEvent(
                    resourceContext,
                    ResourceEventTypes.Events.Deployment.ReplicaMaterializing,
                    $"Materializing replica {replicaPosition} '{instance.Name}' for deployment '{deployment.Id}'.");
            }

            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, instance, replicaGroup),
                action,
                cancellationToken);

            if (deployment is not null)
            {
                AppendDeploymentEvent(
                    resourceContext,
                    ResourceEventTypes.Events.Deployment.ReplicaMaterialized,
                    $"Materialized replica {replicaPosition} '{instance.Name}' for deployment '{deployment.Id}'.");
            }
        }
    }

    public static void AppendDeploymentEvent(
        ResourceProcedureContext context,
        string eventType,
        string message,
        ResourceSignalSeverity severity = ResourceSignalSeverity.Info)
    {
        var effectiveMessage = string.IsNullOrWhiteSpace(context.Cause)
            ? message.Trim()
            : $"{message.Trim().TrimEnd('.')} Cause: {context.Cause.Trim().TrimEnd('.')}.";

        context.ResourceEvents?.Append(new ResourceEvent(
            context.Resource.Id,
            eventType,
            effectiveMessage,
            DateTimeOffset.UtcNow,
            context.TriggeredBy,
            severity));
    }

    private static string FormatReplicaPosition(ResourceOrchestratorServiceInstance instance) =>
        string.Create(
            CultureInfo.InvariantCulture,
            $"{instance.ReplicaOrdinal}/{instance.ReplicaCount}");
}
