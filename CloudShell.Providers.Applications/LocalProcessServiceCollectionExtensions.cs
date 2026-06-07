using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Applications;

public static class LocalProcessServiceCollectionExtensions
{
    public static IServiceCollection AddLocalProcessRunner(
        this IServiceCollection services,
        Action<LocalProcessOptions>? configure = null)
    {
        var options = services.GetOrAddLocalProcessOptions();
        configure?.Invoke(options);

        services.TryAddSingleton<ApplicationRuntimeStateStore>();
        services.TryAddSingleton<LocalProcessRunner>();
        return services;
    }

    private static LocalProcessOptions GetOrAddLocalProcessOptions(this IServiceCollection services)
    {
        var options = services
            .Where(descriptor => descriptor.ServiceType == typeof(LocalProcessOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<LocalProcessOptions>()
            .SingleOrDefault();

        if (options is not null)
        {
            return options;
        }

        options = new LocalProcessOptions();
        services.TryAddSingleton(options);
        return options;
    }
}
