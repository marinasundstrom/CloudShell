using CloudShell.Abstractions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class JavaScriptAppResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseJavaScriptAppResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddJavaScriptAppResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddJavaScriptAppResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == JavaScriptAppResourceTypeProvider.ClassId))
        {
            services.AddSingleton(JavaScriptAppResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, JavaScriptAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, JavaScriptAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, JavaScriptAppResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, JavaScriptAppReferenceGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaScriptAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaScriptAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, JavaScriptAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaScriptAppStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaScriptAppStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, JavaScriptAppRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, JavaScriptAppResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaScriptAppRuntimeEnvironmentProvider,
                JavaScriptAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaScriptAppRuntimeEnvironmentProvider,
                ProjectResourceIdentityEnvironmentResolver>());
        services.TryAddSingleton<JavaScriptAppProcessRuntimeController>();
        services.TryAddSingleton<IJavaScriptAppRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<JavaScriptAppProcessRuntimeController>());
        services.TryAddSingleton<IJavaScriptAppRuntimeOutputReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IJavaScriptAppRuntimeController>()
                    is IJavaScriptAppRuntimeOutputReader outputReader
                    ? outputReader
                    : serviceProvider.GetRequiredService<JavaScriptAppProcessRuntimeController>());
        services.TryAddSingleton<IJavaScriptAppRuntimeMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IJavaScriptAppRuntimeController>()
                    is IJavaScriptAppRuntimeMonitor monitor
                    ? monitor
                    : serviceProvider.GetRequiredService<JavaScriptAppProcessRuntimeController>());

        return services;
    }
}
