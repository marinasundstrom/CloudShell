using CloudShell.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Traefik;

public sealed class TraefikProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.traefik",
        "Traefik",
        "Adds a Traefik load-balancer provider that materializes CloudShell load-balancer routes.",
        "0.1.0",
        ["load-balancer-provider.traefik"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<TraefikProviderOptions>();
        builder.Services.TryAddSingleton<TraefikLoadBalancerProvider>();
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Singleton<CloudShell.Abstractions.ResourceManager.ILoadBalancerProvider, TraefikLoadBalancerProvider>());
    }
}
