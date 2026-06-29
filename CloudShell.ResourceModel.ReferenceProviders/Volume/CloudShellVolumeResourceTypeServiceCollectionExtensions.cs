using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class CloudShellVolumeResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddCloudShellVolumeResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == CloudShellVolumeResourceTypeProvider.ClassId))
        {
            services.AddSingleton(CloudShellVolumeResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, CloudShellVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, CloudShellVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, CloudShellVolumeResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, CloudShellVolumeGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, CloudShellVolumeProvisionOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, CloudShellVolumeProvisionOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, CloudShellVolumeResourceProjectionProvider>());
        services.TryAddSingleton<
            ICloudShellVolumeProvisioner,
            NoopCloudShellVolumeProvisioner>();

        return services;
    }
}
