using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager.Orchestration;

internal static class ResourceOrchestratorProviderResolver
{
    private const string ResourceModelBridgeProviderIdAttribute = "resourceModel.bridgeProviderId";

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
        if (context.Resource.ResourceAttributes.TryGetValue(
                ResourceModelBridgeProviderIdAttribute,
                out var bridgeProviderId) &&
            !string.IsNullOrWhiteSpace(bridgeProviderId))
        {
            var bridgeProvider = context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, bridgeProviderId, StringComparison.OrdinalIgnoreCase))
                as IResourceProcedureProvider;
            if (bridgeProvider is not null)
            {
                return bridgeProvider;
            }
        }

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
        var bridgeProvider = GetBridgeProvider<IResourceOrchestratorServiceProcedureProvider>(
            context,
            provider => provider.CanExecuteOrchestratorService(context.Resource, action));
        if (bridgeProvider is not null)
        {
            return bridgeProvider;
        }

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
        var bridgeProvider = GetBridgeProvider<IResourceOrchestratorDeploymentProvider>(
            context,
            provider => provider.CanDescribeDeployment(context.Resource));
        if (bridgeProvider is not null)
        {
            return bridgeProvider;
        }

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
        var bridgeProvider = GetBridgeProvider<IResourceOrchestratorDeploymentAppliedProvider>(
            context,
            provider => provider.CanHandleDeploymentApplied(context.Resource));
        if (bridgeProvider is not null)
        {
            return bridgeProvider;
        }

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

    public static IResourceOrchestratorDeploymentTearDownProvider? GetDeploymentTearDownProvider(
        ResourceOrchestrationContext context)
    {
        var bridgeProvider = GetBridgeProvider<IResourceOrchestratorDeploymentTearDownProvider>(
            context,
            provider => provider.CanDescribeDeploymentTearDown(context.Resource));
        if (bridgeProvider is not null)
        {
            return bridgeProvider;
        }

        if (context.Registration is not null)
        {
            return context.ResourceManager.Providers.FirstOrDefault(provider =>
                string.Equals(provider.Id, context.Registration.ProviderId, StringComparison.OrdinalIgnoreCase) &&
                provider is IResourceOrchestratorDeploymentTearDownProvider tearDownProvider &&
                tearDownProvider.CanDescribeDeploymentTearDown(context.Resource))
                as IResourceOrchestratorDeploymentTearDownProvider;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.DisplayName, context.Resource.Provider, StringComparison.OrdinalIgnoreCase) &&
            provider is IResourceOrchestratorDeploymentTearDownProvider tearDownProvider &&
            tearDownProvider.CanDescribeDeploymentTearDown(context.Resource))
            as IResourceOrchestratorDeploymentTearDownProvider;
    }

    public static IResourceOrchestratorDeploymentFailureProvider? GetDeploymentFailureProvider(
        ResourceOrchestrationContext context)
    {
        var bridgeProvider = GetBridgeProvider<IResourceOrchestratorDeploymentFailureProvider>(
            context,
            provider => provider.CanHandleDeploymentApplyFailed(context.Resource));
        if (bridgeProvider is not null)
        {
            return bridgeProvider;
        }

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

    private static TProvider? GetBridgeProvider<TProvider>(
        ResourceOrchestrationContext context,
        Func<TProvider, bool> canUse)
        where TProvider : class
    {
        if (!context.Resource.ResourceAttributes.TryGetValue(
                ResourceModelBridgeProviderIdAttribute,
                out var bridgeProviderId) ||
            string.IsNullOrWhiteSpace(bridgeProviderId))
        {
            return null;
        }

        return context.ResourceManager.Providers.FirstOrDefault(provider =>
            string.Equals(provider.Id, bridgeProviderId, StringComparison.OrdinalIgnoreCase) &&
            provider is TProvider typedProvider &&
            canUse(typedProvider))
            as TProvider;
    }
}
