using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class GoAppResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseGoAppResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddGoAppResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddGoAppResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == GoAppResourceTypeProvider.ClassId))
        {
            services.AddSingleton(GoAppResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, GoAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, GoAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, GoAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeploymentArtifactLayoutProvider, GoAppArtifactLayoutProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeploymentArtifactValidationProvider, ApplicationArtifactValidationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityAttributeProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, GoAppReferenceGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, GoAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, GoAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, GoAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, GoAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, GoAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, GoAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, GoAppStartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, GoAppStopExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, GoAppRestartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, GoAppResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IGoAppRuntimeEnvironmentProvider,
                GoAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IGoAppRuntimeEnvironmentProvider,
                ProjectResourceIdentityEnvironmentResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IGoAppRuntimeEnvironmentProvider,
                GoAppServiceDiscoveryEnvironmentResolver>());
        services.TryAddSingleton<GoAppProcessRuntimeController>();
        services.TryAddSingleton<IGoAppRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<GoAppProcessRuntimeController>());
        services.TryAddSingleton<IGoAppRuntimeOutputReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IGoAppRuntimeController>()
                    is IGoAppRuntimeOutputReader outputReader
                    ? outputReader
                    : serviceProvider.GetRequiredService<GoAppProcessRuntimeController>());
        services.TryAddSingleton<IGoAppRuntimeMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IGoAppRuntimeController>()
                    is IGoAppRuntimeMonitor monitor
                    ? monitor
                    : serviceProvider.GetRequiredService<GoAppProcessRuntimeController>());

        return services;
    }
}
