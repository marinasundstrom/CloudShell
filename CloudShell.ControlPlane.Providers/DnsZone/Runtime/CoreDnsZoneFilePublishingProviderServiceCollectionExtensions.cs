using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class CoreDnsZoneFilePublishingProviderServiceCollectionExtensions
{
    public static IServiceCollection AddCoreDnsZoneFilePublishingProvider(
        this IServiceCollection services,
        Action<CoreDnsZoneFilePublishingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<CoreDnsZoneFilePublishingProvider>();
        services.AddSingleton<INamePublishingProvider>(
            serviceProvider => serviceProvider.GetRequiredService<CoreDnsZoneFilePublishingProvider>());

        return services;
    }
}
