using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
        builder.Services.TryAddSingleton<TraefikLoadBalancerProvider>();
        builder.Services.TryAddSingleton<ILoadBalancerProvider>(
            serviceProvider => serviceProvider.GetRequiredService<TraefikLoadBalancerProvider>());
        builder.Services.TryAddSingleton<ILoadBalancerRuntimeProvider>(
            serviceProvider => serviceProvider.GetRequiredService<TraefikLoadBalancerProvider>());
        builder.Services.Replace(
            ServiceDescriptor.Singleton<ILoadBalancerConfigurationApplier, ResourceModelGraphTraefikLoadBalancerConfigurationApplier>());
    }
}
