using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class DefaultResourceOrchestrator :
    IResourceOrchestrator,
    IResourceOrchestratorDeploymentApplier
{
    public string Id => "default";

    public string DisplayName => "Default";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        GetServiceProcedureProvider(context, action) is not null ||
        GetProcedureProvider(context) is not null;

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var serviceProvider = GetServiceProcedureProvider(context, action);
        if (serviceProvider is not null)
        {
            return ExecuteOrchestratorServiceActionAsync(
                serviceProvider,
                context,
                action,
                cancellationToken);
        }

        var provider = GetProcedureProvider(context)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));

        return provider.ExecuteActionAsync(
            CreateProcedureContext(context),
            action,
            cancellationToken);
    }

    public bool CanDelete(ResourceOrchestrationContext context) =>
        GetDirectProcedureProvider(context) is not null;

    public Task<ResourceProcedureResult> DeleteAsync(
        ResourceOrchestrationContext context,
        CancellationToken cancellationToken = default)
    {
        var provider = GetDirectProcedureProvider(context)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceDeleteUnsupported(context.Resource.Name));

        return provider.DeleteAsync(
            CreateProcedureContext(context),
            cancellationToken);
    }

    public bool CanApplyDeployment(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment) =>
        GetServiceProcedureProvider(context, ResourceAction.Start) is not null &&
        string.Equals(deployment.OrchestratorId, Id, StringComparison.OrdinalIgnoreCase);

    public async Task<ResourceOrchestratorDeploymentApplyResult> ApplyDeploymentAsync(
        ResourceOrchestrationContext context,
        ResourceOrchestratorDeployment deployment,
        CancellationToken cancellationToken = default)
    {
        var provider = GetServiceProcedureProvider(context, ResourceAction.Start)
            ?? throw new ControlPlaneException(
                ControlPlaneError.ResourceActionUnsupported(context.Resource.Name));
        var resourceContext = CreateProcedureContext(context);
        await ExecuteOrchestratorServiceActionCoreAsync(
            provider,
            resourceContext,
            deployment.Spec.Service,
            ResourceAction.Start,
            cancellationToken);

        var applied = deployment with { Status = ResourceOrchestratorDeploymentStatus.Active };
        return new ResourceOrchestratorDeploymentApplyResult(
            applied,
            ResourceProcedureResult.Completed(
                $"Applied deployment '{deployment.Id}' for revision '{deployment.RevisionId}'."));
    }

    private static async Task<ResourceProcedureResult> ExecuteOrchestratorServiceActionAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        var resourceContext = CreateProcedureContext(context);
        var service = await provider.CreateOrchestratorServiceAsync(
            resourceContext,
            cancellationToken);

        if (action.Kind == ResourceActionKind.Restart)
        {
            await ExecuteOrchestratorServiceActionCoreAsync(
                provider,
                resourceContext,
                service,
                action with { Kind = ResourceActionKind.Stop },
                cancellationToken);
            await ExecuteOrchestratorServiceActionCoreAsync(
                provider,
                resourceContext,
                service,
                action with { Kind = ResourceActionKind.Start },
                cancellationToken);
            return ResourceProcedureResult.Completed($"Restarted {context.Resource.Name}.");
        }

        await ExecuteOrchestratorServiceActionCoreAsync(
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

    private static async Task ExecuteOrchestratorServiceActionCoreAsync(
        IResourceOrchestratorServiceProcedureProvider provider,
        ResourceProcedureContext resourceContext,
        ResourceOrchestratorService service,
        ResourceAction action,
        CancellationToken cancellationToken)
    {
        await provider.PrepareOrchestratorServiceAsync(
            new ResourceOrchestratorServiceProcedureContext(resourceContext, service),
            action,
            cancellationToken);

        foreach (var instance in ResourceOrchestratorServiceInstances.CreateDefaultInstances(service))
        {
            await provider.ExecuteOrchestratorServiceInstanceAsync(
                new ResourceOrchestratorServiceInstanceContext(resourceContext, service, instance),
                action,
                cancellationToken);
        }
    }

    private static ResourceProcedureContext CreateProcedureContext(
        ResourceOrchestrationContext context) =>
        new(
            context.Resource,
            context.Registration,
            context.ResourceGroup?.Id,
            context.Registrations,
            context.ResourceManager,
            context.PreferredContainerHostId,
            context.TriggeredBy,
            context.Cause,
            context.ResourceEvents);

    private static IResourceProcedureProvider? GetProcedureProvider(
        ResourceOrchestrationContext context)
    {
        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceProcedureProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private static IResourceProcedureProvider? GetDirectProcedureProvider(
        ResourceOrchestrationContext context)
    {
        if (context.Registration is null)
        {
            return null;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase))
            as IResourceProcedureProvider;
    }

    private static IResourceOrchestratorServiceProcedureProvider? GetServiceProcedureProvider(
        ResourceOrchestrationContext context,
        ResourceAction action)
    {
        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider is IResourceOrchestratorServiceProcedureProvider serviceProvider &&
                serviceProvider.CanExecuteOrchestratorService(context.Resource, action))
                as IResourceOrchestratorServiceProcedureProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            provider is IResourceOrchestratorServiceProcedureProvider serviceProvider &&
            serviceProvider.CanExecuteOrchestratorService(context.Resource, action))
            as IResourceOrchestratorServiceProcedureProvider;
    }

}
