using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.Abstractions.ResourceManager;

public static class ContainerEngineServiceCollectionExtensions
{
    public static ICloudShellBuilder UseContainerEngine(
        this ICloudShellBuilder builder,
        string id,
        string name,
        ContainerEngineKind kind,
        string endpoint,
        bool isDefault = false,
        string registry = ContainerRegistryDefaults.Default)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContainerEngine(
            new ContainerEngineResourceDefinition(
                id,
                name,
                kind,
                endpoint,
                isDefault,
                registry));
    }

    public static IControlPlaneBuilder UseContainerEngine(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        ContainerEngineKind kind,
        string endpoint,
        bool isDefault = false,
        string registry = ContainerRegistryDefaults.Default)
    {
        ((ICloudShellBuilder)builder).UseContainerEngine(id, name, kind, endpoint, isDefault, registry);
        return builder;
    }

    public static ICloudShellBuilder UseContainerEngine(
        this ICloudShellBuilder builder,
        ContainerEngineResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);

        var provider = new StaticContainerHostProvider(Normalize(definition));
        builder.Services.AddSingleton<IContainerEngineProvider>(provider);
        builder.Services.AddSingleton<IContainerHostProvider>(provider);
        return builder;
    }

    public static IControlPlaneBuilder UseContainerEngine(
        this IControlPlaneBuilder builder,
        ContainerEngineResourceDefinition definition)
    {
        ((ICloudShellBuilder)builder).UseContainerEngine(definition);
        return builder;
    }

    private static ContainerEngineResourceDefinition Normalize(ContainerEngineResourceDefinition definition) =>
        definition with
        {
            Id = definition.Id.Trim(),
            Name = definition.Name.Trim(),
            Endpoint = definition.Endpoint.Trim(),
            Registry = string.IsNullOrWhiteSpace(definition.Registry)
                ? ContainerRegistryDefaults.Default
                : definition.Registry.Trim(),
            RegistryCredentials = ContainerRegistryCredentials.Normalize(definition.RegistryCredentials)
        };

    private sealed class StaticContainerHostProvider(
        ContainerEngineResourceDefinition definition) : IContainerEngineProvider, IContainerHostProvider
    {
        public ContainerEngineResourceDefinition GetContainerEngine() => definition;

        public ContainerHostDescriptor GetDefaultHost() => definition.ToContainerHostDescriptor();
    }
}
