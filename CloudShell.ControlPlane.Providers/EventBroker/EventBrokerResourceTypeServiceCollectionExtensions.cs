using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class EventBrokerResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseEventBrokerResourceProvider(
        this IControlPlaneBuilder builder,
        Action<EventBrokerRuntimeOptions>? configureRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configureRuntime is null)
        {
            builder.Services.AddEventBrokerResourceType();
        }
        else
        {
            builder.Services.AddEventBrokerResourceType(configureRuntime);
        }

        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddEventBrokerResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddProviderExecutionDispatcher();

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == EventBrokerResourceTypeProvider.ClassId))
        {
            services.AddSingleton(EventBrokerResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, EventBrokerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, EventBrokerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, EventBrokerResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, EventBrokerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, EventBrokerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, EventBrokerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, EventBrokerStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, EventBrokerStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, EventBrokerRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, EventBrokerStartExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, EventBrokerStopExecutionHandler>());
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IProviderExecutionHandler, EventBrokerRestartExecutionHandler>());
        services.TryAddSingleton<EventBrokerRuntimeOptions>();
        services.TryAddSingleton<EventBrokerProcessRuntimeController>();
        services.TryAddSingleton<IEventBrokerRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<EventBrokerProcessRuntimeController>());
        services.TryAddSingleton<IEventBrokerRuntimeMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<EventBrokerProcessRuntimeController>());

        return services;
    }

    public static IServiceCollection AddEventBrokerResourceType(
        this IServiceCollection services,
        Action<EventBrokerRuntimeOptions> configureRuntime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRuntime);

        var options = new EventBrokerRuntimeOptions();
        configureRuntime(options);

        services.AddSingleton(options);
        return services.AddEventBrokerResourceType();
    }
}
