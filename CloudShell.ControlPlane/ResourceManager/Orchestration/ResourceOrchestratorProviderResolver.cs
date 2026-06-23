using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

internal static class ResourceOrchestratorProviderResolver
{
    public static ResourceProcedureContext CreateProcedureContext(
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

    public static IResourceProcedureProvider? GetProcedureProvider(
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

    public static IResourceProcedureProvider? GetDirectProcedureProvider(
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

    public static IResourceOrchestratorServiceProcedureProvider? GetServiceProcedureProvider(
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

    public static IResourceOrchestratorDeploymentProvider? GetDeploymentProvider(
        ResourceOrchestrationContext context)
    {
        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider is IResourceOrchestratorDeploymentProvider deploymentProvider &&
                deploymentProvider.CanDescribeDeployment(context.Resource))
                as IResourceOrchestratorDeploymentProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            provider is IResourceOrchestratorDeploymentProvider deploymentProvider &&
            deploymentProvider.CanDescribeDeployment(context.Resource))
            as IResourceOrchestratorDeploymentProvider;
    }

    public static IResourceOrchestratorDeploymentAppliedProvider? GetDeploymentAppliedProvider(
        ResourceOrchestrationContext context)
    {
        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider is IResourceOrchestratorDeploymentAppliedProvider appliedProvider &&
                appliedProvider.CanHandleDeploymentApplied(context.Resource))
                as IResourceOrchestratorDeploymentAppliedProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            provider is IResourceOrchestratorDeploymentAppliedProvider appliedProvider &&
            appliedProvider.CanHandleDeploymentApplied(context.Resource))
            as IResourceOrchestratorDeploymentAppliedProvider;
    }

    public static IResourceOrchestratorDeploymentFailureProvider? GetDeploymentFailureProvider(
        ResourceOrchestrationContext context)
    {
        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider is IResourceOrchestratorDeploymentFailureProvider failureProvider &&
                failureProvider.CanHandleDeploymentApplyFailed(context.Resource))
                as IResourceOrchestratorDeploymentFailureProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            provider is IResourceOrchestratorDeploymentFailureProvider failureProvider &&
            failureProvider.CanHandleDeploymentApplyFailed(context.Resource))
            as IResourceOrchestratorDeploymentFailureProvider;
    }
}
