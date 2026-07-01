using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ResourceModelGraphEndpointMappingReconcilerServiceCollectionExtensions
{
    public static IServiceCollection AddResourceModelGraphEndpointMappingReconciler(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ResourceModelGraphEndpointMappingReconciler>();
        services.Replace(ServiceDescriptor.Singleton<INetworkEndpointMappingReconciler>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphEndpointMappingReconciler>()));
        services.Replace(ServiceDescriptor.Singleton<IVirtualNetworkEndpointMappingReconciler>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphEndpointMappingReconciler>()));
        services.Replace(ServiceDescriptor.Singleton<ILocalHostNetworkEndpointMappingReconciler>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphEndpointMappingReconciler>()));
        services.Replace(ServiceDescriptor.Singleton<IMacOSHostNetworkEndpointMappingReconciler>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphEndpointMappingReconciler>()));

        return services;
    }
}
