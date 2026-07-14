using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ProviderExecutionServiceCollectionExtensions
{
    public static IServiceCollection AddProviderExecutionDispatcher(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<
            IProviderExecutionObservationStore,
            InMemoryProviderExecutionObservationStore>();
        services.TryAddSingleton<IProviderExecutionDispatcher>(serviceProvider =>
            new InProcessProviderExecutionDispatcher(
                serviceProvider.GetRequiredService<IServiceScopeFactory>(),
                serviceProvider.GetService<IProviderExecutionObservationStore>()));

        return services;
    }
}
