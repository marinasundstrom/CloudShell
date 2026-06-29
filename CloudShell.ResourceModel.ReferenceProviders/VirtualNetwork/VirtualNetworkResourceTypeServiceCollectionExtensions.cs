using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class VirtualNetworkResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddVirtualNetworkResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == VirtualNetworkResourceTypeProvider.ClassId))
        {
            services.AddSingleton(VirtualNetworkResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, VirtualNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, VirtualNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, VirtualNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, VirtualNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, VirtualNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, VirtualNetworkResourceProjectionProvider>());
        services.TryAddSingleton<
            IVirtualNetworkEndpointMappingReconciler,
            NoopVirtualNetworkEndpointMappingReconciler>();

        return services;
    }
}
