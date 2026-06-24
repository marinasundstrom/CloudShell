using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class ContainerApplicationResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddContainerApplicationResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == ContainerApplicationResourceTypeProvider.ClassId))
        {
            services.AddSingleton(ContainerApplicationResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationImageUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationImageUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, ContainerApplicationResourceProjectionProvider>());

        return services;
    }
}
