using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class AspNetCoreProjectResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseAspNetCoreProjectResourceProvider(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddAspNetCoreProjectResourceType();
        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddAspNetCoreProjectResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddNetworkingEndpointGraphShapes();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceAttributeValueShapeProvider, AspNetCoreProjectShapeProvider>());

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == AspNetCoreProjectResourceTypeProvider.ClassId))
        {
            services.AddSingleton(AspNetCoreProjectResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, AspNetCoreProjectResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, AspNetCoreProjectResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, AspNetCoreProjectResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeploymentArtifactLayoutProvider, AspNetCoreProjectArtifactLayoutProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IDeploymentArtifactValidationProvider, ApplicationArtifactValidationProvider>());
        services.TryAddSingleton<IApplicationArtifactFolderResolver, ApplicationArtifactFolderResolver>();
        services.TryAddSingleton<IApplicationArtifactMaterializer, ApplicationArtifactMaterializer>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProvider, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceCapabilityProjector, VolumeConsumerCapabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, VolumeConsumerGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionGraphValidator, AspNetCoreProjectReferenceGraphValidator>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceGraphDependencyProvider, VolumeConsumerGraphDependencyProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, AspNetCoreProjectStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, AspNetCoreProjectStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, AspNetCoreProjectRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, AspNetCoreProjectStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, AspNetCoreProjectStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, AspNetCoreProjectRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, AspNetCoreProjectResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAspNetCoreProjectRuntimeEnvironmentProvider,
                AspNetCoreProjectServiceDiscoveryEnvironmentResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAspNetCoreProjectRuntimeEnvironmentProvider,
                ProjectResourceIdentityEnvironmentResolver>());
        services.TryAddSingleton<AspNetCoreProjectProcessRuntimeController>();
        services.TryAddSingleton<IAspNetCoreProjectRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<AspNetCoreProjectProcessRuntimeController>());
        services.TryAddSingleton<IAspNetCoreProjectRuntimeOutputReader>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IAspNetCoreProjectRuntimeController>()
                    is IAspNetCoreProjectRuntimeOutputReader outputReader
                    ? outputReader
                    : serviceProvider.GetRequiredService<AspNetCoreProjectProcessRuntimeController>());
        services.TryAddSingleton<IAspNetCoreProjectRuntimeMonitor>(
            serviceProvider =>
                serviceProvider.GetRequiredService<IAspNetCoreProjectRuntimeController>()
                    is IAspNetCoreProjectRuntimeMonitor monitor
                    ? monitor
                    : serviceProvider.GetRequiredService<AspNetCoreProjectProcessRuntimeController>());

        return services;
    }
}
