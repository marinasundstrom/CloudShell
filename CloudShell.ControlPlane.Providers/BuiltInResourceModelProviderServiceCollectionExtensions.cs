using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Providers;

public sealed class BuiltInResourceModelProviderOptions
{
    public string ResourceGraphProviderId { get; set; } =
        ResourceModelResourceProvider.DefaultProviderId;

    public string ResourceGraphProviderDisplayName { get; set; } = "Resource model";

    public ResourceDefinitionResolutionContext? ResourceDefinitionResolutionContext { get; set; }

    public ResourceModelResourceManagerProjectionOptions? ResourceManagerProjectionOptions { get; set; }
}

public static class BuiltInResourceModelProviderServiceCollectionExtensions
{
    public static IControlPlaneBuilder UseBuiltInResourceModelRuntimeAdapters(
        this IControlPlaneBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddBuiltInResourceModelRuntimeAdapters();

        return builder;
    }

    public static IServiceCollection AddBuiltInResourceModelRuntimeAdapters(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services
            .AddResourceModelGraphEndpointMappingReconciler()
            .AddResourceModelGraphDnsZoneNameMappingReconciler();

        return services;
    }

    public static IControlPlaneBuilder UseBuiltInResourceModelProviders(
        this IControlPlaneBuilder builder,
        Action<BuiltInResourceModelProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = CreateOptions(configure);
        builder.Services.AddBuiltInResourceModelProviderTypes(options);

        builder.UseResourceGraphIntegration(
            options.ResourceGraphProviderId,
            options.ResourceGraphProviderDisplayName,
            options.ResourceDefinitionResolutionContext,
            options.ResourceManagerProjectionOptions);

        return builder;
    }

    public static IServiceCollection AddBuiltInResourceModelProviderTypes(
        this IServiceCollection services,
        Action<BuiltInResourceModelProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        return services.AddBuiltInResourceModelProviderTypes(CreateOptions(configure));
    }

    private static IServiceCollection AddBuiltInResourceModelProviderTypes(
        this IServiceCollection services,
        BuiltInResourceModelProviderOptions options)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(options);

        services
            .AddExecutableApplicationResourceType()
            .AddAspNetCoreProjectResourceType()
            .AddJavaScriptAppResourceType()
            .AddJavaAppResourceType()
            .AddGoAppResourceType()
            .AddPythonAppResourceType()
            .AddContainerApplicationResourceType()
            .AddDockerHostResourceType()
            .AddDockerContainerResourceType()
            .AddContainerHostResourceType()
            .AddStorageResourceType()
            .AddCloudShellVolumeResourceType()
            .AddSqlServerResourceType()
            .AddSqlDatabaseResourceType()
            .AddRabbitMQResourceType()
            .AddEventBrokerResourceType()
            .AddDeviceRegistryResourceType()
            .AddHostConfigurationSourceResourceType()
            .AddLocalVolumeResourceType()
            .AddIdentityProvisioningResourceType()
            .AddNetworkResourceType()
            .AddVirtualNetworkResourceType()
            .AddLocalHostNetworkResourceType()
            .AddMacOSHostNetworkResourceType()
            .AddDnsZoneResourceType()
            .AddNameMappingResourceType()
            .AddLoadBalancerResourceType()
            .AddServiceResourceType();

        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();

        return services;
    }

    private static BuiltInResourceModelProviderOptions CreateOptions(
        Action<BuiltInResourceModelProviderOptions>? configure)
    {
        var options = new BuiltInResourceModelProviderOptions();
        configure?.Invoke(options);

        return options;
    }
}
