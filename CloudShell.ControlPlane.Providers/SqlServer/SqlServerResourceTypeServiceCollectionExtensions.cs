using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class SqlServerResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseStorageBackedSqlServerResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddStorageBackedSqlServerResourceTypes();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddStorageBackedSqlServerResourceTypes(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddSqlServerResourceType();

        return services;
    }

    public static IServiceCollection AddSqlServerResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.AddContainerHostResourceType();

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

    public static IServiceCollection AddLocalSqlServerDockerRuntime(
        this IServiceCollection services,
        Action<LocalSqlServerDockerRuntimeOptions>? configure = null,
        Action<LocalExecutableResourceOrchestrationDescriptorOptions>? configureOrchestrationDescriptors = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<
            ILocalSqlServerDockerCommandRunner,
            ProcessLocalSqlServerDockerCommandRunner>();
        services.TryAddSingleton<
            ILocalSqlServerReadinessProbe,
            ResourceModelSqlServerReadinessProbe>();
        services.Replace(ServiceDescriptor.Singleton<
            ISqlServerRuntimeHandler,
            LocalSqlServerDockerRuntimeHandler>());
        if (configureOrchestrationDescriptors is not null)
        {
            services.AddLocalExecutableResourceOrchestrationDescriptors(configureOrchestrationDescriptors);
        }

        return services;
    }
}
