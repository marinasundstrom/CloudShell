using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ControlPlane.Providers;

public sealed class BuiltInResourceModelProviderOptions
{
    public Action<ConfigurationStoreRuntimeOptions>? ConfigureConfigurationStoreRuntime { get; set; }

    public Action<SecretsVaultRuntimeOptions>? ConfigureSecretsVaultRuntime { get; set; }
}

public static class BuiltInResourceModelProviderServiceCollectionExtensions
{
    public static IServiceCollection AddBuiltInResourceModelProviderTypes(
        this IServiceCollection services,
        Action<BuiltInResourceModelProviderOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new BuiltInResourceModelProviderOptions();
        configure?.Invoke(options);

        services
            .AddExecutableApplicationResourceType()
            .AddAspNetCoreProjectResourceType()
            .AddLocalContainerApplicationResourceTypes()
            .AddDockerContainerResourceType()
            .AddContainerHostResourceType()
            .AddStorageBackedSqlServerResourceTypes()
            .AddSqlDatabaseResourceType()
            .AddHostConfigurationSourceResourceType()
            .AddStorageResourceType()
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

        if (options.ConfigureConfigurationStoreRuntime is { } configureConfigurationStore)
        {
            services.AddConfigurationStoreResourceType(configureConfigurationStore);
        }
        else
        {
            services.AddConfigurationStoreResourceType();
        }

        if (options.ConfigureSecretsVaultRuntime is { } configureSecretsVault)
        {
            services.AddSecretsVaultResourceType(configureSecretsVault);
        }
        else
        {
            services.AddSecretsVaultResourceType();
        }

        return services;
    }
}
