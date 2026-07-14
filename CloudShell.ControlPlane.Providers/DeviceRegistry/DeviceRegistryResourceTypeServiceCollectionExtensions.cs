using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class DeviceRegistryResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseDeviceRegistryResourceProvider(
        this IControlPlaneBuilder builder,
        Action<DeviceRegistryRuntimeOptions>? configureRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configureRuntime is null)
        {
            builder.Services.AddDeviceRegistryResourceType();
        }
        else
        {
            builder.Services.AddDeviceRegistryResourceType(configureRuntime);
        }

        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddDeviceRegistryResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == DeviceRegistryResourceTypeProvider.ClassId))
        {
            services.AddSingleton(DeviceRegistryResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, DeviceRegistryResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, DeviceRegistryResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, DeviceRegistryResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DeviceRegistryStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DeviceRegistryStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, DeviceRegistryRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DeviceRegistryStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DeviceRegistryStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, DeviceRegistryRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, DeviceRegistryStartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, DeviceRegistryStopExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IProviderExecutionHandler, DeviceRegistryRestartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, DeviceRegistryResourceProjectionProvider>());
        services.TryAddSingleton<DeviceRegistryRuntimeOptions>();
        services.TryAddSingleton<IDeviceRegistryRuntimeDeviceManager, DeviceRegistryRuntimeDeviceManager>();
        services.TryAddSingleton<DeviceRegistryProcessRuntimeController>();
        services.TryAddSingleton<IDeviceRegistryRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<DeviceRegistryProcessRuntimeController>());
        services.TryAddSingleton<IDeviceRegistryRuntimeMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<DeviceRegistryProcessRuntimeController>());

        return services;
    }

    public static IServiceCollection AddDeviceRegistryResourceType(
        this IServiceCollection services,
        Action<DeviceRegistryRuntimeOptions> configureRuntime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRuntime);

        var options = new DeviceRegistryRuntimeOptions();
        configureRuntime(options);

        services.AddSingleton(options);
        return services.AddDeviceRegistryResourceType();
    }
}
