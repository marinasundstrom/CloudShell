using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.configuration",
        "Configuration Store",
        "Adds configuration store resources for non-secret setting references.",
        "0.1.0",
        ["resource-type.configuration.store", "resource-trait.environment-variables"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.AddLocalProcessRunner();
        builder.Services.TryAddSingleton<ConfigurationProviderOptions>();
        builder.Services.TryAddSingleton<ConfigurationStore>();
        builder.Services.TryAddSingleton<IConfiguration>(
            _ => new ConfigurationBuilder().Build());
        builder.Services.AddSingleton<IResourceEnvironmentVariableProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ConfigurationResourceProvider>());
        builder.Services.AddSingleton<IConfigurationEntryReferenceResolver>(
            serviceProvider => serviceProvider.GetRequiredService<ConfigurationResourceProvider>());
        builder.Services.AddSingleton<IConfigurationEntryReferenceResolver>(
            serviceProvider => serviceProvider.GetRequiredService<HostConfigurationSourceProvider>());

        builder
            .AddResourceProvider<ConfigurationResourceProvider>()
            .AddResourceProvider<HostConfigurationSourceProvider>()
            .AddLogProvider<ConfigurationResourceProvider>()
            .AddResourceType<Pages.RegisterConfigurationStore>(
                "configuration.store",
                "Configuration service",
                "Create a local configuration service for non-secret settings that dependent resources can consume.",
                "key",
                15,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            "/healthz",
                            EndpointName: "entries",
                            Name: "health")
                    ]),
                resourceClass: ResourceClass.Configuration)
            .AddResourceTab<Pages.ConfigurationStoreOverview>(
                "configuration.store",
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateConfigurationStore>(
                "configuration.store",
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true);
    }
}

public sealed class SecretsProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.secrets",
        "Secrets Provider",
        "Adds Secrets Vault resources and secret reference resolution.",
        "0.1.0",
        ["resource-type.secrets.vault", "resource-trait.environment-variables"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.AddLocalProcessRunner();
        builder.Services.TryAddSingleton<ConfigurationProviderOptions>();
        builder.Services.TryAddSingleton<SecretsVaultStore>();
        builder.Services.AddSingleton<ISecretReferenceResolver>(
            serviceProvider => serviceProvider.GetRequiredService<SecretsVaultProvider>());

        builder
            .AddResourceProvider<SecretsVaultProvider>()
            .AddLogProvider<SecretsVaultProvider>()
            .AddResourceType<Pages.RegisterSecretsVault, Pages.UpdateSecretsVault>(
                SecretsVaultProvider.ResourceType,
                "Secrets Vault",
                "Create a Secrets Vault for provider-owned secret references.",
                "lock-closed",
                16,
                resourceClass: ResourceClass.SecretsVault)
            .AddResourceTab<Pages.SecretsVaultOverview>(
                SecretsVaultProvider.ResourceType,
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateSecretsVault>(
                SecretsVaultProvider.ResourceType,
                "secrets",
                "Secrets",
                20,
                showsApplyButton: true);
    }
}
