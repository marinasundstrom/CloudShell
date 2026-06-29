using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceModel.ReferenceProviders;

public static class LoadBalancerResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddLoadBalancerResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceAttributeValueShapeProvider, LoadBalancerShapeProvider>());

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == LoadBalancerResourceTypeProvider.ClassId))
        {
            services.AddSingleton(LoadBalancerResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, LoadBalancerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, LoadBalancerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, LoadBalancerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, LoadBalancerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, LoadBalancerApplyConfigurationOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, LoadBalancerApplyConfigurationOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, LoadBalancerResourceProjectionProvider>());
        services.TryAddSingleton<
            ILoadBalancerConfigurationApplier,
            NoopLoadBalancerConfigurationApplier>();

        return services;
    }
}
