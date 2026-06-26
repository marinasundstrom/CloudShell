using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders;

public static class SqlServerResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddSqlServerResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == SqlServerResourceTypeProvider.ClassId))
        {
            services.AddSingleton(SqlServerResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, SqlServerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, SqlServerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, SqlServerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, SqlServerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SqlServerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SqlServerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SqlServerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SqlServerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SqlServerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SqlServerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SqlServerReconcileAccessOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SqlServerReconcileAccessOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, SqlServerResourceProjectionProvider>());
        services.TryAddSingleton<
            ISqlServerRuntimeHandler,
            NoopSqlServerRuntimeHandler>();
        services.TryAddSingleton<
            ISqlServerAccessReconciler,
            NoopSqlServerAccessReconciler>();

        return services;
    }
}
