using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class LocalHostNetworkResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddLocalHostNetworkResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == LocalHostNetworkResourceTypeProvider.ClassId))
        {
            services.AddSingleton(LocalHostNetworkResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, LocalHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, LocalHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, LocalHostNetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, LocalHostNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, LocalHostNetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, LocalHostNetworkResourceProjectionProvider>());
        services.TryAddSingleton<
            ILocalHostNetworkEndpointMappingReconciler,
            NoopLocalHostNetworkEndpointMappingReconciler>();

        return services;
    }
}
