using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ResourceModelGraphDnsZoneNameMappingReconcilerServiceCollectionExtensions
{
    public static IServiceCollection AddResourceModelGraphDnsZoneNameMappingReconciler(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ResourceModelGraphDnsZoneNameMappingReconciler>();
        services.Replace(ServiceDescriptor.Singleton<IDnsZoneNameMappingReconciler>(
            serviceProvider => serviceProvider.GetRequiredService<ResourceModelGraphDnsZoneNameMappingReconciler>()));

        return services;
    }
}
