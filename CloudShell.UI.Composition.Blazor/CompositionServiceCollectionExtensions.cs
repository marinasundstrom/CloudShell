using CloudShell.UI.Composition;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.UI.Composition.Blazor;

public static class CompositionServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellUiComposition(
        this IServiceCollection services,
        Action<CompositionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);

        services.AddSingleton(CompositionRegistry.Create(configure));
        return services;
    }
}
