using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class ConfigurationStoreResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseConfigurationStoreResourceProvider(
        this IControlPlaneBuilder builder,
        Action<ConfigurationStoreRuntimeOptions>? configureRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configureRuntime is null)
        {
            builder.Services.AddConfigurationStoreResourceType();
        }
        else
        {
            builder.Services.AddConfigurationStoreResourceType(configureRuntime);
        }

        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddConfigurationStoreResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == ConfigurationStoreResourceTypeProvider.ClassId))
        {
            services.AddSingleton(ConfigurationStoreResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, ConfigurationStoreResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, ConfigurationStoreResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, ConfigurationStoreResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ConfigurationStoreStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ConfigurationStoreStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ConfigurationStoreRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, ConfigurationStoreInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ConfigurationStoreStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ConfigurationStoreStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ConfigurationStoreRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, ConfigurationStoreInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, ConfigurationStoreResourceProjectionProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, ConfigurationStoreStartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, ConfigurationStoreStopExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, ConfigurationStoreRestartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, ConfigurationStoreInspectExecutionHandler>());
        services.TryAddSingleton<ConfigurationStoreRuntimeOptions>();
        services.TryAddSingleton<
            IConfigurationStoreRuntimeSettingManager,
            ConfigurationStoreRuntimeSettingManager>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceModelGraphApplyReconciler, ConfigurationStoreSeedReconciler>());
        services.TryAddSingleton<ConfigurationStoreProcessRuntimeController>();
        services.TryAddSingleton<IConfigurationStoreRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<ConfigurationStoreProcessRuntimeController>());
        services.TryAddSingleton<IConfigurationStoreRuntimeMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<ConfigurationStoreProcessRuntimeController>());
        services.TryAddSingleton<
            IConfigurationStoreInspector,
            ConfigurationStoreRuntimeInspector>();

        return services;
    }

    public static IServiceCollection AddConfigurationStoreResourceType(
        this IServiceCollection services,
        Action<ConfigurationStoreRuntimeOptions> configureRuntime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRuntime);

        var options = new ConfigurationStoreRuntimeOptions();
        configureRuntime(options);

        services.AddSingleton(options);
        return services.AddConfigurationStoreResourceType();
    }
}
