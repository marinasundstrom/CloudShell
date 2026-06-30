using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ContainerApplicationResourceTypeServiceCollectionExtensions
{
    public static IServiceCollection AddLocalContainerApplicationResourceTypes(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();

        return services;
    }

    public static IServiceCollection AddContainerApplicationResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.AddContainerHostResourceType();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == ContainerApplicationResourceTypeProvider.ClassId))
        {
            services.AddSingleton(ContainerApplicationResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, ContainerApplicationResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, ContainerApplicationGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddSingleton<
            IContainerApplicationRuntimeHandler,
            NoopContainerApplicationRuntimeHandler>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationImageUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ContainerApplicationReplicasUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationImageUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ContainerApplicationReplicasUpdateOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, ContainerApplicationResourceProjectionProvider>());

        return services;
    }

    public static IServiceCollection AddLocalContainerApplicationProcessRuntime(
        this IServiceCollection services,
        Action<LocalContainerApplicationProcessRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.TryAddSingleton<LocalContainerApplicationProcessRuntimeBridge>();
        services.Replace(ServiceDescriptor.Singleton<
            IContainerApplicationRuntimeHandler,
            LocalContainerApplicationProcessRuntimeHandler>());

        return services;
    }

    public static IServiceCollection AddDeferredContainerApplicationRuntime(
        this IServiceCollection services,
        Action<DeferredContainerApplicationRuntimeOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            services.Configure(configure);
        }

        services.Replace(ServiceDescriptor.Singleton<
            IContainerApplicationRuntimeHandler,
            DeferredContainerApplicationRuntimeHandler>());

        return services;
    }

    public static IServiceCollection AddDelegatingContainerApplicationRuntime(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<DelegatingContainerApplicationRuntimeHandler>();
        services.Replace(ServiceDescriptor.Singleton<IContainerApplicationRuntimeHandler>(
            serviceProvider => serviceProvider.GetRequiredService<DelegatingContainerApplicationRuntimeHandler>()));
        services.Replace(ServiceDescriptor.Singleton<IContainerApplicationOrchestratorRuntimeHandler>(
            serviceProvider => serviceProvider.GetRequiredService<DelegatingContainerApplicationRuntimeHandler>()));

        return services;
    }
}
