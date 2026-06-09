using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Providers.Traefik;

public static class TraefikProviderServiceCollectionExtensions
{
    public static ICloudShellBuilder AddTraefikProvider(
        this ICloudShellBuilder builder,
        Action<TraefikProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddTraefikProviderCore(builder, configure);
        return builder.AddExtension(new TraefikProviderExtension(), activationPolicy);
    }

    public static IControlPlaneBuilder AddTraefikProvider(
        this IControlPlaneBuilder builder,
        Action<TraefikProviderOptions>? configure = null,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        AddTraefikProviderCore(builder, configure);
        return builder.AddExtension(new TraefikProviderExtension(), activationPolicy);
    }

    private static void AddTraefikProviderCore(
        ICloudShellBuilder builder,
        Action<TraefikProviderOptions>? configure)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = builder.Services
            .Where(descriptor => descriptor.ServiceType == typeof(TraefikProviderOptions))
            .Select(descriptor => descriptor.ImplementationInstance)
            .OfType<TraefikProviderOptions>()
            .SingleOrDefault();

        if (options is null)
        {
            options = new TraefikProviderOptions();
            builder.Services.AddSingleton(options);
        }

        configure?.Invoke(options);
    }
}
