using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreShell.Blazor;

public static class CoreShellBlazorServiceCollectionExtensions
{
    public static IServiceCollection AddCoreShellBlazor(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<ICoreShellContentResolver, BlazorCoreShellContentResolver>();
        services.TryAddSingleton<ICoreShellLayoutResolver, BlazorCoreShellLayoutResolver>();
        services.TryAddSingleton<ICoreShellBlazorPageProjectionService, CoreShellBlazorPageProjectionService>();
        services.TryAddSingleton<
            ICoreShellBlazorSectionOutletProjectionService,
            CoreShellBlazorSectionOutletProjectionService>();

        return services;
    }
}
