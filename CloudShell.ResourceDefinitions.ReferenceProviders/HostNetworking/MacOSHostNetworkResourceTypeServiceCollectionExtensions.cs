using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class MacOSHostNetworkResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddMacOSHostNetworkResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == MacOSHostNetworkResourceTypeProvider.ClassId))
        {
            services.AddSingleton(MacOSHostNetworkResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, MacOSHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, MacOSHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, MacOSHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, MacOSHostNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, MacOSHostNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, MacOSHostNetworkResourceProjectionProvider>());

        return services;
    }
}
