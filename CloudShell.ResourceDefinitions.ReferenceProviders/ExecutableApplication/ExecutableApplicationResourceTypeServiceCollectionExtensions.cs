using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class ExecutableApplicationResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddExecutableApplicationResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == ExecutableApplicationResourceTypeProvider.ClassId))
        {
            services.AddSingleton(ExecutableApplicationResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, ExecutableApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, ExecutableApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, ExecutableApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddSingleton<
            IExecutableApplicationRuntimeController,
            ExecutableApplicationProcessRuntimeController>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ExecutableStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ExecutableStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, ExecutableApplicationResourceProjectionProvider>());

        return services;
    }
}
