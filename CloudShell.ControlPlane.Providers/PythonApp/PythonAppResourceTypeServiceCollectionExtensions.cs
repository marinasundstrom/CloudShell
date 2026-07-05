using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class PythonAppResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UsePythonAppResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddPythonAppResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddPythonAppResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == PythonAppResourceTypeProvider.ClassId))
        {
            services.AddSingleton(PythonAppResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, PythonAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, PythonAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, PythonAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, PythonAppReferenceGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, PythonAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, PythonAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, PythonAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, PythonAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, PythonAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, PythonAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, PythonAppResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPythonAppRuntimeEnvironmentProvider,
                PythonAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPythonAppRuntimeEnvironmentProvider,
                ProjectResourceIdentityEnvironmentResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IPythonAppRuntimeEnvironmentProvider,
                PythonAppServiceDiscoveryEnvironmentResolver>());
        services.TryAddSingleton<PythonAppProcessRuntimeController>();
        services.TryAddSingleton<IPythonAppRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<PythonAppProcessRuntimeController>());
        services.TryAddSingleton<IPythonAppRuntimeOutputReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IPythonAppRuntimeController>()
                    is IPythonAppRuntimeOutputReader outputReader
                    ? outputReader
                    : serviceProvider.GetRequiredService<PythonAppProcessRuntimeController>());
        services.TryAddSingleton<IPythonAppRuntimeMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IPythonAppRuntimeController>()
                    is IPythonAppRuntimeMonitor monitor
                    ? monitor
                    : serviceProvider.GetRequiredService<PythonAppProcessRuntimeController>());

        return services;
    }
}
