using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class RabbitMQResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseRabbitMQResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddRabbitMQResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddRabbitMQResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.AddContainerHostResourceType();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == RabbitMQResourceTypeProvider.ClassId))
        {
            services.AddSingleton(RabbitMQResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, RabbitMQResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, RabbitMQResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, RabbitMQResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, RabbitMQStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, RabbitMQStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, RabbitMQStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, RabbitMQStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, RabbitMQRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, RabbitMQRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, RabbitMQReconcileAccessOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, RabbitMQReconcileAccessOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, RabbitMQResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourcePermissionGrantStatusProvider, RabbitMQPermissionGrantStatusProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceModelResourceManagerEndpointProjectionProvider, RabbitMQResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceModelResourceManagerStateProvider, RabbitMQResourceManagerStateProvider>());
        services.TryAddSingleton<
            IRabbitMQAccessReconciler,
            NoopRabbitMQAccessReconciler>();
        services.TryAddSingleton<
            IRabbitMQBrokerTopologyProvider,
            NoopRabbitMQBrokerTopologyProvider>();
        services.TryAddSingleton<
            IRabbitMQPrincipalCredentialProvider,
            DefaultRabbitMQPrincipalCredentialProvider>();
        services.TryAddSingleton<
            IRabbitMQBootstrapCredentialProvider,
            InMemoryRabbitMQBootstrapCredentialProvider>();
        services.TryAddScoped<RabbitMQCredentialResolver>();
        services.TryAddSingleton<
            IRabbitMQRuntimeHandler,
            NoopRabbitMQRuntimeHandler>();

        return services;
    }

    public static IServiceCollection AddLocalRabbitMQDockerRuntime(
        this IServiceCollection services,
        Action<LocalRabbitMQDockerRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<
            ILocalRabbitMQDockerCommandRunner,
            ProcessLocalRabbitMQDockerCommandRunner>();
        services.Replace(ServiceDescriptor.Singleton<
            IRabbitMQRuntimeHandler,
            LocalRabbitMQDockerRuntimeHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<ILogProvider, LocalRabbitMQDockerRuntimeLogProvider>());
        services.AddRabbitMQManagementApiAccessReconciler();

        return services;
    }

    public static IServiceCollection AddRabbitMQManagementApiAccessReconciler(
        this IServiceCollection services,
        Action<RabbitMQManagementAccessOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHttpClient(RabbitMQManagementApiAccessReconciler.HttpClientName);
        services.AddOptions<RabbitMQManagementAccessOptions>()
            .BindConfiguration(RabbitMQManagementAccessOptions.SectionName);
        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<
            IRabbitMQPrincipalCredentialProvider,
            DefaultRabbitMQPrincipalCredentialProvider>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IRabbitMQPermissionGrantEffectivenessProvider, RabbitMQManagementApiPermissionGrantEffectivenessProvider>());
        services.Replace(ServiceDescriptor.Singleton<
            IRabbitMQAccessReconciler,
            RabbitMQManagementApiAccessReconciler>());
        services.Replace(ServiceDescriptor.Singleton<
            IRabbitMQBrokerTopologyProvider,
            RabbitMQManagementApiBrokerTopologyProvider>());

        return services;
    }
}
