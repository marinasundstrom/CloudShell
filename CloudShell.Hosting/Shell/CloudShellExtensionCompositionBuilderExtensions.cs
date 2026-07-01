using CoreShell.Composition;
using CoreShell.Composition.Blazor;

namespace CloudShell.Abstractions.Extensions;

public static class CloudShellExtensionCompositionBuilderExtensions
{
    public static ICloudShellExtensionBuilder AddCompositionModule(
        this ICloudShellExtensionBuilder builder,
        CompositionModuleId id,
        Action<CompositionModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddCloudShellUiCompositionModule(id, configure);
        return builder;
    }

    public static ICloudShellExtensionBuilder AddCompositionModule<TContext>(
        this ICloudShellExtensionBuilder builder,
        CompositionModuleId id,
        Action<TContext, CompositionModuleBuilder> configure)
        where TContext : ICompositionHostContext
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(configure);

        builder.Services.AddCloudShellUiCompositionModule(id, configure);
        return builder;
    }
}
