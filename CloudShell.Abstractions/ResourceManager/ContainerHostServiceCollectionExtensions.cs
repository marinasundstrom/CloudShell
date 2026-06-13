using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerHostServiceCollectionExtensions
{
    public static ICloudShellBuilder UseContainerHost(
        this ICloudShellBuilder builder,
        string id,
        string name,
        ContainerHostKind kind,
        string endpoint,
        bool isDefault = false,
        string registry = ContainerRegistryDefaults.Default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContainerHost(
            new ContainerHostDescriptor(
                id,
                name,
                kind,
                endpoint,
                isDefault,
                registry));
    }

    public static IControlPlaneBuilder UseContainerHost(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        ContainerHostKind kind,
        string endpoint,
        bool isDefault = false,
        string registry = ContainerRegistryDefaults.Default)
    {
        ((ICloudShellBuilder)builder).UseContainerHost(id, name, kind, endpoint, isDefault, registry);
        return builder;
    }

    public static ICloudShellBuilder UseContainerHost(
        this ICloudShellBuilder builder,
        ContainerHostDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(descriptor);

        builder.Services.AddSingleton<IContainerHostProvider>(
            new StaticContainerHostProvider(Normalize(descriptor)));
        return builder;
    }

    public static IControlPlaneBuilder UseContainerHost(
        this IControlPlaneBuilder builder,
        ContainerHostDescriptor descriptor)
    {
        ((ICloudShellBuilder)builder).UseContainerHost(descriptor);
        return builder;
    }

    private static ContainerHostDescriptor Normalize(ContainerHostDescriptor descriptor) =>
        descriptor with
        {
            Id = descriptor.Id.Trim(),
            Name = descriptor.Name.Trim(),
            Endpoint = descriptor.Endpoint.Trim(),
            Registry = string.IsNullOrWhiteSpace(descriptor.Registry)
                ? ContainerRegistryDefaults.Default
                : descriptor.Registry.Trim(),
            RegistryCredentials = ContainerRegistryCredentials.Normalize(descriptor.RegistryCredentials)
        };

    private sealed class StaticContainerHostProvider(ContainerHostDescriptor descriptor) : IContainerHostProvider
    {
        public ContainerHostDescriptor GetDefaultHost() => descriptor;
    }
}
