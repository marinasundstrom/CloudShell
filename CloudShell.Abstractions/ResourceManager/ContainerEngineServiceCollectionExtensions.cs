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
        bool isDefault = false)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseContainerEngine(
            new ContainerEngineResourceDefinition(
                id,
                name,
                kind,
                endpoint,
                isDefault));
    }

    public static IControlPlaneBuilder UseContainerEngine(
        this IControlPlaneBuilder builder,
        string id,
        string name,
        ContainerEngineKind kind,
        string endpoint,
        bool isDefault = false)
    {
        ((ICloudShellBuilder)builder).UseContainerEngine(id, name, kind, endpoint, isDefault);
        return builder;
    }

    public static ICloudShellBuilder UseContainerEngine(
        this ICloudShellBuilder builder,
        ContainerEngineResourceDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);

        builder.Services.AddSingleton<IContainerEngineProvider>(
            new StaticContainerEngineProvider(Normalize(definition)));
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
            Endpoint = definition.Endpoint.Trim()
        };

    private sealed class StaticContainerEngineProvider(
        ContainerEngineResourceDefinition definition) : IContainerEngineProvider
    {
        public ContainerEngineResourceDefinition GetContainerEngine() => definition;
    }
}
