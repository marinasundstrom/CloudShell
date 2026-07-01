using CoreShell.Composition;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CoreShell.Composition.Blazor;

public static class CompositionServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellUiComposition(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(serviceProvider =>
            new CompositionEngineHost(serviceProvider.GetServices<CompositionModule>()));
        services.TryAddSingleton(serviceProvider =>
            serviceProvider.GetRequiredService<CompositionEngineHost>().Registry);

        return services;
    }

    public static IServiceCollection AddCloudShellUiComposition(
        this IServiceCollection services,
        Action<CompositionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(CompositionRegistry.Create(configure));
        return services;
    }

    public static IServiceCollection AddCloudShellUiComposition(
        this IServiceCollection services,
        IEnumerable<CompositionModule> modules)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(modules);

        foreach (var module in modules)
        {
            services.AddCloudShellUiCompositionModule(module);
        }

        return services.AddCloudShellUiComposition();
    }

    public static IServiceCollection AddCloudShellUiCompositionModule(
        this IServiceCollection services,
        CompositionModule module)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(module);

        services.AddSingleton(module);
        return services;
    }

    public static IServiceCollection AddCloudShellUiCompositionModule(
        this IServiceCollection services,
        CompositionModuleId id,
        Action<CompositionModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        return services.AddCloudShellUiCompositionModule(
            CompositionModule.Create(id, configure));
    }

    public static IServiceCollection AddCloudShellUiCompositionModule<TContext>(
        this IServiceCollection services,
        CompositionModuleId id,
        Action<TContext, CompositionModuleBuilder> configure)
        where TContext : ICompositionHostContext
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(serviceProvider =>
        {
            var context = serviceProvider.GetRequiredService<TContext>();
            return CompositionModule.Create(id, module => configure(context, module));
        });

        return services;
    }
}
