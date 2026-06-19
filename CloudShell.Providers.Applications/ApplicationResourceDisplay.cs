using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public static class ApplicationResourceDisplay
{
    public static string GetLifetimeLabel(ApplicationLifetime lifetime) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => "Control plane scoped",
            _ => "Detached"
        };

    public static string GetContainerHostLabel(ContainerHostDescriptor host) =>
        host.IsDefault ? $"{host.Name} (default)" : host.Name;
}
