using CoreShell;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.Extensions;

public static class CloudShellExtensionCoreShellBuilderExtensions
{
    public static ICloudShellExtensionBuilder AddCoreShellModule(
        this ICloudShellExtensionBuilder builder,
        CoreShellModuleId id,
        Action<CoreShellModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddSingleton(CoreShellModule.Create(id, configure));
        return builder;
    }

    public static ICloudShellExtensionBuilder AddCoreShellModule<TContext>(
        this ICloudShellExtensionBuilder builder,
        CoreShellModuleId id,
        Action<TContext, CoreShellModuleBuilder> configure)
        where TContext : notnull
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddSingleton(serviceProvider =>
        {
            var context = serviceProvider.GetRequiredService<TContext>();
            return CoreShellModule.Create(id, module => configure(context, module));
        });

        return builder;
    }
}
