using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ContainerHostResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddContainerHostResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == ContainerHostResourceTypeProvider.ClassId))
        {
            services.AddSingleton(ContainerHostResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, ContainerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, ContainerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, ContainerHostResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerHostInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerHostInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, ContainerHostInspectExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, ContainerHostResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOrchestrationDescriptorProvider, ContainerHostOrchestrationDescriptorProvider>());
        services.TryAddSingleton<IContainerHostInspector, NoopContainerHostInspector>();

        return services;
    }
}
