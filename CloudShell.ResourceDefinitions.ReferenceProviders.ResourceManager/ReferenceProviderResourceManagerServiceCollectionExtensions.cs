using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public static class ReferenceProviderResourceManagerServiceCollectionExtensions
{
    public static IServiceCollection AddReferenceProviderResourceManagerProjections(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                ConfigurationStoreResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                SecretsVaultResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                ConfigurationStoreResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                SecretsVaultResourceManagerStateProvider>());

        return services;
    }
}
