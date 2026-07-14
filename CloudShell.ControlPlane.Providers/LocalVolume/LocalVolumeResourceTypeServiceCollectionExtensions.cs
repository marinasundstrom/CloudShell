using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class LocalVolumeResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddLocalVolumeResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == LocalVolumeResourceTypeProvider.ClassId))
        {
            services.AddSingleton(LocalVolumeResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, LocalVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, LocalVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, LocalVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, LocalVolumeProvisionOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, LocalVolumeProvisionOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, LocalVolumeResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, LocalVolumeProvisionExecutionHandler>());
        services.TryAddSingleton<ILocalVolumeProvisioner, NoopLocalVolumeProvisioner>();

        return services;
    }
}
