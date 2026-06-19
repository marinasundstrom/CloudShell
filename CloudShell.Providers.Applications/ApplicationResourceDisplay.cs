using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Providers.Applications;

public static class ApplicationResourceDisplay
{
    public static string GetLifetimeLabel(ApplicationLifetime lifetime) =>
        GetLifetimeLabel(lifetime, static value => value);

    public static string GetLifetimeLabel(ApplicationLifetime lifetime, Func<string, string> localize) =>
        lifetime switch
        {
            ApplicationLifetime.ControlPlaneScoped => localize("Control plane scoped"),
            _ => localize("Detached")
        };

    public static string GetContainerHostLabel(ContainerHostDescriptor host) =>
        GetContainerHostLabel(host, static value => value);

    public static string GetContainerHostLabel(ContainerHostDescriptor host, Func<string, string> localize) =>
        host.IsDefault ? $"{host.Name} ({localize("default")})" : host.Name;
}
