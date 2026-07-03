using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.ControlPlane.Providers;

public static class SecretsVaultResourceTypeServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseSecretsVaultResourceProvider(
        this IControlPlaneBuilder builder,
        Action<SecretsVaultRuntimeOptions>? configureRuntime = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        if (configureRuntime is null)
        {
            builder.Services.AddSecretsVaultResourceType();
        }
        else
        {
            builder.Services.AddSecretsVaultResourceType(configureRuntime);
        }

        builder.Services.AddResourceGraphIntegration();

        return builder;
    }

    public static IServiceCollection AddSecretsVaultResourceType(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        if (!services.Any(descriptor =>
                descriptor.ServiceType == typeof(ResourceClassDefinition) &&
                descriptor.ImplementationInstance is ResourceClassDefinition classDefinition &&
                classDefinition.ClassId == SecretsVaultResourceTypeProvider.ClassId))
        {
            services.AddSingleton(SecretsVaultResourceTypeProvider.ClassDefinition);
        }

        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceTypeProvider, SecretsVaultResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceChangeApplyProvider, SecretsVaultResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceDefinitionApplyProvider, SecretsVaultResourceTypeProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SecretsVaultStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SecretsVaultStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SecretsVaultRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProvider, SecretsVaultInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SecretsVaultStartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SecretsVaultStopOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SecretsVaultRestartOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceOperationProjector, SecretsVaultInspectOperationProvider>());
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceProjectionProvider, SecretsVaultResourceProjectionProvider>());
        services.TryAddSingleton<SecretsVaultRuntimeOptions>();
        services.TryAddSingleton<
            ISecretsVaultRuntimeSecretManager,
            SecretsVaultRuntimeSecretManager>();
        services.TryAddEnumerable(
            ServiceDescriptor.Singleton<IResourceModelGraphApplyReconciler, SecretsVaultSeedReconciler>());
        services.TryAddSingleton<SecretsVaultProcessRuntimeController>();
        services.TryAddSingleton<ISecretsVaultRuntimeController>(
            serviceProvider => serviceProvider.GetRequiredService<SecretsVaultProcessRuntimeController>());
        services.TryAddSingleton<ISecretsVaultRuntimeMonitor>(
            serviceProvider => serviceProvider.GetRequiredService<SecretsVaultProcessRuntimeController>());
        services.TryAddSingleton<ISecretsVaultInspector, SecretsVaultRuntimeInspector>();

        return services;
    }

    public static IServiceCollection AddSecretsVaultResourceType(
        this IServiceCollection services,
        Action<SecretsVaultRuntimeOptions> configureRuntime)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configureRuntime);

        var options = new SecretsVaultRuntimeOptions();
        configureRuntime(options);

        services.AddSingleton(options);
        return services.AddSecretsVaultResourceType();
    }
}
