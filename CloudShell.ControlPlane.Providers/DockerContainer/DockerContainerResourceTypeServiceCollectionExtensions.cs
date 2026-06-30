using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class DockerContainerResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddDockerContainerResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == DockerContainerResourceTypeProvider.ClassId))
        {
            services.AddSingleton(DockerContainerResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, DockerContainerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, DockerContainerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, DockerContainerResourceTypeProvider>());
        services.TryAddSingleton<
            IDockerContainerRuntimeHandler,
            NoopDockerContainerRuntimeHandler>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerContainerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerContainerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerContainerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerContainerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerContainerPauseOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerContainerPauseOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerContainerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerContainerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerContainerUnpauseOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerContainerUnpauseOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, DockerContainerResourceProjectionProvider>());

        return services;
    }
}
