using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class BuiltInProviderResourceManagerServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseResourceGraphIntegration(
        this IControlPlaneBuilder builder,
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

    public static IServiceCollection AddResourceGraphIntegration(
        this IServiceCollection services,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceGraphIntegrationRegistration)))
        {
            return services;
        }

        services.AddSingleton(new ResourceGraphIntegrationRegistration(id, displayName));
        services.AddResourceModelGraphServices();
        services.AddBuiltInResourceModelRuntimeAdapters();
        services.AddBuiltInProviderResourceManagerIntegration(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return services;
    }

    public static IServiceCollection AddBuiltInProviderResourceManagerIntegration(
        this IServiceCollection services,
        string id = ResourceModelResourceProvider.DefaultProviderId,
        string displayName = "Resource model",
        ResourceDefinitionResolutionContext? resolutionContext = null,
        ResourceModelResourceManagerProjectionOptions? projectionOptions = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        services.AddBuiltInProviderResourceManagerProjections();
        services.AddResourceModelGraphProcedureProvider(
            id,
            displayName,
            resolutionContext,
            projectionOptions);

        return services;
    }

    public static IServiceCollection AddBuiltInProviderResourceManagerProjections(
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
                JavaScriptAppResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                JavaAppResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                GoAppResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerEndpointProjectionProvider,
                ContainerApplicationResourceManagerEndpointProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<
                IResourceModelGraphMaterializedChangeApplier,
                ContainerApplicationResourceModelGraphMaterializedChangeApplier>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelGraphDeploymentDescriptor,
                ContainerApplicationResourceModelGraphDeploymentDescriptor>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelGraphOrchestratorServiceExecutor,
                ContainerApplicationResourceModelGraphServiceExecutor>());
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
                JavaScriptAppResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                JavaAppResourceManagerStateProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerStateProvider,
                GoAppResourceManagerStateProvider>());
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
                IResourceModelResourceManagerObservabilityProvider,
                JavaScriptAppResourceManagerObservabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerObservabilityProvider,
                JavaAppResourceManagerObservabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerObservabilityProvider,
                GoAppResourceManagerObservabilityProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                NameMappingResourceManagerProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                SqlDatabaseResourceManagerProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                SqlServerResourceManagerAttributeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                ConfigurationStoreResourceManagerAttributeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                SecretsVaultResourceManagerAttributeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerAttributeProvider,
                LoadBalancerResourceManagerAttributeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceModelResourceManagerParentProvider,
                NameMappingResourceManagerProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IAspNetCoreProjectRuntimeEnvironmentProvider,
                AspNetCoreProjectEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaScriptAppRuntimeEnvironmentProvider,
                JavaScriptAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IJavaAppRuntimeEnvironmentProvider,
                JavaAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IGoAppRuntimeEnvironmentProvider,
                GoAppEnvironmentReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IConfigurationEntryReferenceResolver,
                ConfigurationStoreRuntimeEntryReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ISecretReferenceResolver,
                SecretsVaultRuntimeSecretReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ICertificateReferenceResolver,
                SecretsVaultRuntimeSecretReferenceResolver>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                AspNetCoreProjectResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                JavaScriptAppResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                JavaAppResourceManagerMonitoringProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                IResourceMonitoringProvider,
                GoAppResourceManagerMonitoringProvider>());
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
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILogProvider,
                JavaScriptAppResourceManagerLogProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILogProvider,
                JavaAppResourceManagerLogProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<
                ILogProvider,
                GoAppResourceManagerLogProvider>());
        services.AddScoped<
            ISqlDatabaseServerResolver,
            SqlDatabaseResourceManagerServerResolver>();

        return services;
    }

    private sealed record ResourceGraphIntegrationRegistration(string Id, string DisplayName);
}
