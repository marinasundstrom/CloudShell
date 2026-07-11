using CoreShell.Composition;
using CoreShell.Composition.Blazor;
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
        services.TryAddSingleton<ICoreShellNotificationService, EmptyCoreShellNotificationService>();
        services.TryAddSingleton<ICoreShellNotificationProducer, EmptyCoreShellNotificationProducer>();
        services.TryAddSingleton<ICoreShellToastService, EmptyCoreShellToastService>();
        services.TryAddSingleton<ICoreShellBlazorPageProjectionService, CoreShellBlazorPageProjectionService>();
        services.TryAddSingleton<
            ICoreShellBlazorSectionOutletProjectionService,
            CoreShellBlazorSectionOutletProjectionService>();

        return services;
    }

    public static IServiceCollection AddCoreShellBlazorHost(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddCoreShellBlazor();
        services.AddCloudShellUiComposition();
        services.TryAddSingleton<CoreShellBlazorCompositionModuleFactory>();
        services.AddSingleton<CompositionModule>(serviceProvider =>
            serviceProvider
                .GetRequiredService<CoreShellBlazorCompositionModuleFactory>()
                .CreateModule(serviceProvider.GetServices<CoreShellModule>()));

        return services;
    }
}
