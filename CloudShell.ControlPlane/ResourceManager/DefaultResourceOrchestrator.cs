using CloudShell.Abstractions.ResourceManager;
using System.Text.Json;

namespace CloudShell.ControlPlane.ResourceManager;

public sealed class DefaultResourceOrchestrator(
    IEnumerable<IResourceOrchestrationDescriptorProvider> descriptorProviders) : IResourceOrchestrator
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly IReadOnlyList<IResourceOrchestrationDescriptorProvider> descriptorProviders =
        descriptorProviders.ToArray();

    public string Id => "default";

    public string DisplayName => "Default";

    public bool CanExecute(
        ResourceOrchestrationContext context,
        ResourceAction action) =>
        GetProcedureProvider(context) is not null &&
        !IsContainerBackedWorkload(context);

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
                context.Registrations),
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
                context.Registrations),
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

    private bool IsContainerBackedWorkload(ResourceOrchestrationContext context)
    {
        var provider = descriptorProviders.FirstOrDefault(provider =>
            provider.CanDescribe(context.Resource));
        if (provider is null)
        {
            return false;
        }

        try
        {
            var descriptor = provider.DescribeAsync(
                    context.Resource,
                    new ResourceOrchestrationDescriptorContext(
                        context.Registration,
                        context.ResourceGroup,
                        context.ResourceManager))
                .GetAwaiter()
                .GetResult();
            var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(SerializerOptions);
            return workload?.Kind is ResourceWorkloadKind.ContainerImage or ResourceWorkloadKind.ContainerBuild;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
