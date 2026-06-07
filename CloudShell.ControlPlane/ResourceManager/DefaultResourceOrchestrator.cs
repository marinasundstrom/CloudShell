using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class DefaultResourceOrchestrator : IResourceOrchestrator
{
    public string Id => "default";

    public string DisplayName => "Default";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        GetProcedureProvider(context) is not null;

    public Task<ResourceProcedureResult> ExecuteActionAsync(
        ResourceOrchestrationContext context,
        ResourceAction action,
        CancellationToken cancellationToken = default)
    {
        var provider = GetProcedureProvider(context)
            ?? throw new InvalidOperationException(
                $"Resource '{context.Resource.Name}' does not support actions.");

        return provider.ExecuteActionAsync(
            new ResourceProcedureContext(
                context.Resource,
                context.Registration,
                context.ResourceGroup?.Id,
                context.Registrations,
                context.ResourceManager,
                context.PreferredContainerEngineId),
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
            ?? throw new InvalidOperationException(
                $"Resource '{context.Resource.Name}' does not support delete.");

        return provider.DeleteAsync(
            new ResourceProcedureContext(
                context.Resource,
                context.Registration,
                context.ResourceGroup?.Id,
                context.Registrations,
                context.ResourceManager,
                context.PreferredContainerEngineId),
            cancellationToken);
    }

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

}
