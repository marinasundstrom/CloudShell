using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.Hosting;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;

public static class ReferenceProviderResourceManagerServiceCollectionExtensions
{
    public static ICloudShellBuilder UseResourceGraphIntegration(
        this ICloudShellBuilder builder,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddResourceGraphIntegration(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return builder;
    }

    public static IControlPlaneBuilder UseResourceGraphIntegration(
        this IControlPlaneBuilder builder,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ((ICloudShellBuilder)builder).UseResourceGraphIntegration(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return builder;
    }

    public static IServiceCollection AddResourceGraphIntegration(
        this IServiceCollection services,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddResourceModelGraphServices();
        services.AddReferenceProviderResourceManagerIntegration(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return services;
    }

    public static IServiceCollection AddReferenceProviderResourceManagerIntegration(
        this IServiceCollection services,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        services.AddReferenceProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return services;
    }

    public static IServiceCollection AddReferenceProviderResourceManagerProjections(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                AspNetCoreProjectResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                ContainerApplicationResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                ConfigurationStoreResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                SecretsVaultResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                SqlServerResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                LoadBalancerResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                VirtualNetworkResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                AspNetCoreProjectResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                ContainerApplicationResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                DockerContainerResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                ConfigurationStoreResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                SecretsVaultResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                SqlServerResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerObservabilityProvider,
                AspNetCoreProjectResourceManagerObservabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAspNetCoreProjectRuntimeEnvironmentProvider,
                AspNetCoreProjectEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                AspNetCoreProjectResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                ExecutableApplicationResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                ConfigurationStoreResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                SecretsVaultResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILogProvider,
                AspNetCoreProjectResourceManagerLogProvider>());
        services.AddScoped<
            ISqlDatabaseServerResolver,
            SqlDatabaseResourceManagerServerResolver>();

        return services;
    }
}
