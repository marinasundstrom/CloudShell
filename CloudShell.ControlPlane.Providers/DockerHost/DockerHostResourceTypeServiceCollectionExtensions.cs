using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class DockerHostResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseDockerHostResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddDockerHostResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddDockerHostResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == DockerHostResourceTypeProvider.ClassId))
        {
            services.AddSingleton(DockerHostResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, DockerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, DockerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, DockerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DockerHostInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DockerHostInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, DockerHostInspectExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, DockerHostResourceProjectionProvider>());
        services.TryAddSingleton<IDockerHostInspector, NoopDockerHostInspector>();

        return services;
    }
}
