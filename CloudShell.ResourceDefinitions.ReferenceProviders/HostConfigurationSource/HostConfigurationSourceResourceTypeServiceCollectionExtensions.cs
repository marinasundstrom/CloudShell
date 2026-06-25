using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class HostConfigurationSourceResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddHostConfigurationSourceResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == HostConfigurationSourceResourceTypeProvider.ClassId))
        {
            services.AddSingleton(HostConfigurationSourceResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, HostConfigurationSourceResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, HostConfigurationSourceResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, HostConfigurationSourceResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, HostConfigurationSourceInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, HostConfigurationSourceInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, HostConfigurationSourceResourceProjectionProvider>());
        services.TryAddSingleton<
            IHostConfigurationSourceInspector,
            NoopHostConfigurationSourceInspector>();

        return services;
    }
}
