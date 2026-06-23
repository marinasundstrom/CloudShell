using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

public sealed class DefaultResourceOrchestrator :
    IResourceOrchestrator,
    IResourceOrchestratorServiceTearDown,
    IResourceOrchestratorReplicaGroupTearDown
{
    public string Id => "default";

    public string DisplayName => "Default";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, action) is not null ||
        ResourceOrchestratorProviderResolver.GetProcedureProvider(context) is not null;

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var serviceProvider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, action);
        if (serviceProvider is not null)
        {
            return ExecuteOrchestratorServiceActionAsync(
                serviceProvider,
                context,
                action,
                cancellationToken);
        }

        var provider = ResourceOrchestratorProviderResolver.GetProcedureProvider(context)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));

        return provider.ExecuteActionAsync(
            ResourceOrchestratorProviderResolver.CreateProcedureContext(context),
            action,
            cancellationToken);
    }

    public bool CanDelete(ResourceOrchestrationContext context) =>
        ResourceOrchestratorProviderResolver.GetDirectProcedureProvider(context) is not null;

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = ResourceOrchestratorProviderResolver.GetDirectProcedureProvider(context)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceDeleteUnsupported(context.Resource.Name));

        return provider.DeleteAsync(
            ResourceOrchestratorProviderResolver.CreateProcedureContext(context),
            cancellationToken);
    }

    public bool CanTearDownService(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service) =>
        string.Equals(context.Resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase) &&
        ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Stop) is not null;

    public async Task<ResourceProcedureResult> TearDownServiceAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Service '{service.Name}' belongs to resource '{service.ResourceId}', not '{context.Resource.Id}'."));
        }

        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Stop)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
            provider,
            ResourceOrchestratorProviderResolver.CreateProcedureContext(context),
            service,
            ResourceAction.Stop,
            cancellationToken);
        return ResourceProcedureResult.Completed($"Tore down service '{service.Name}' for {context.Resource.Name}.");
    }

    public bool CanTearDownReplicaGroup(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup) =>
        string.Equals(context.Resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(service.Name, replicaGroup.ServiceId, StringComparison.OrdinalIgnoreCase) &&
        ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Stop) is not null;

    public async Task<ResourceProcedureResult> TearDownReplicaGroupAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorService service,
        ResourceOrchestratorReplicaGroup replicaGroup,
        CancellationToken cancellationToken = default)
    {
        if (!string.Equals(context.Resource.Id, service.ResourceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Replica group '{replicaGroup.Id}' belongs to service '{service.Name}' for resource '{service.ResourceId}', not '{context.Resource.Id}'."));
        }

        if (!string.Equals(service.Name, replicaGroup.ServiceId, StringComparison.OrdinalIgnoreCase))
        {
            throw new ControlPlaneException(
                ControlPlaneError.InvalidRequest(
                    $"Replica group '{replicaGroup.Id}' belongs to service '{replicaGroup.ServiceId}', not '{service.Name}'."));
        }

        var provider = ResourceOrchestratorProviderResolver.GetServiceProcedureProvider(context, ResourceAction.Stop)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        await ResourceOrchestratorServiceExecutor.ExecuteReplicaGroupAsync(
            provider,
            ResourceOrchestratorProviderResolver.CreateProcedureContext(context),
            service,
            replicaGroup,
            ResourceAction.Stop,
            deployment: null,
            cancellationToken);
        return ResourceProcedureResult.Completed(
            $"Tore down replica group '{replicaGroup.Id}' for service '{service.Name}'.");
    }

    private static async Task<ResourceProcedureResult> ExecuteOrchestratorServiceActionAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var resourceContext = ResourceOrchestratorProviderResolver.CreateProcedureContext(context);
        var service = await provider.CreateOrchestratorServiceAsync(
            resourceContext,
            cancellationToken);

        if (action.Kind == ResourceActionKind.Restart)
        {
            await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
                provider,
                resourceContext,
                service,
                action with { Kind = ResourceActionKind.Stop },
                cancellationToken);
            await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
                provider,
                resourceContext,
                service,
                action with { Kind = ResourceActionKind.Start },
                cancellationToken);
            return ResourceProcedureResult.Completed($"Restarted {context.Resource.Name}.");
        }

        await ResourceOrchestratorServiceExecutor.ExecuteServiceActionAsync(
            provider,
            resourceContext,
            service,
            action,
            cancellationToken);
        return action.Kind switch
        {
            ResourceActionKind.Start => ResourceProcedureResult.Completed($"Started {context.Resource.Name}."),
            ResourceActionKind.Stop => ResourceProcedureResult.Completed($"Stopped {context.Resource.Name}."),
            _ => ResourceProcedureResult.Completed($"Executed {action.DisplayName} for {context.Resource.Name}.")
        };
    }
}
