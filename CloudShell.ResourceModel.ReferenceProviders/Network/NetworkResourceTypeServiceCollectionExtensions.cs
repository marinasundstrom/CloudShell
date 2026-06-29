using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class NetworkResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddNetworkResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == NetworkResourceTypeProvider.ClassId))
        {
            services.AddSingleton(NetworkResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, NetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, NetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, NetworkResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, NetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, NetworkReconcileEndpointMappingsOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, NetworkResourceProjectionProvider>());
        services.TryAddSingleton<
            INetworkEndpointMappingReconciler,
            NoopNetworkEndpointMappingReconciler>();

        return services;
    }
}
