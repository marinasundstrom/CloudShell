using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class JavaAppResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseJavaAppResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddJavaAppResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddJavaAppResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == JavaAppResourceTypeProvider.ClassId))
        {
            services.AddSingleton(JavaAppResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, JavaAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, JavaAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, JavaAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeploymentArtifactLayoutProvider, JavaAppArtifactLayoutProvider>());
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
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, JavaAppReferenceGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, JavaAppResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaAppRuntimeEnvironmentProvider,
                JavaAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaAppRuntimeEnvironmentProvider,
                ProjectResourceIdentityEnvironmentResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaAppRuntimeEnvironmentProvider,
                JavaAppServiceDiscoveryEnvironmentResolver>());
        services.TryAddSingleton<JavaAppProcessRuntimeController>();
        services.TryAddSingleton<IJavaAppRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<JavaAppProcessRuntimeController>());
        services.TryAddSingleton<IJavaAppRuntimeOutputReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IJavaAppRuntimeController>()
                    is IJavaAppRuntimeOutputReader outputReader
                    ? outputReader
                    : serviceProvider.GetRequiredService<JavaAppProcessRuntimeController>());
        services.TryAddSingleton<IJavaAppRuntimeMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IJavaAppRuntimeController>()
                    is IJavaAppRuntimeMonitor monitor
                    ? monitor
                    : serviceProvider.GetRequiredService<JavaAppProcessRuntimeController>());

        return services;
    }
}
