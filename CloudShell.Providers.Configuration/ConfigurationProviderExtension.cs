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
                "Configuration Store",
                "Create a local Configuration Store for setting references that dependent resources can consume.",
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
                ResourceStandardViewIds.Overview,
                "Overview",
                10,
                groupTitle: "General")
            .AddResourceTab<Pages.UpdateConfigurationStore>(
                "configuration.store",
                new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                "Settings",
                20,
                showsApplyButton: true,
                groupTitle: "General")
            .AddResourceTab<Pages.ConfigurationStoreEntries>(
                "configuration.store",
                new ResourceViewId(ResourceTabGroupIds.Entries, "entries"),
                "Entries",
                30,
                showsApplyButton: true,
                groupTitle: "Entries");
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
        builder.Services.AddSingleton<IResourceEnvironmentVariableProvider>(
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
                ResourceStandardViewIds.Overview,
                "Overview",
                10,
                groupTitle: "General")
            .AddResourceTab<Pages.UpdateSecretsVault>(
                SecretsVaultProvider.ResourceType,
                new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                "Settings",
                20,
                showsApplyButton: true,
                groupTitle: "General")
            .AddResourceTab<Pages.SecretsVaultSecrets>(
                SecretsVaultProvider.ResourceType,
                new ResourceViewId(ResourceTabGroupIds.Secrets, "secrets"),
                "Secrets",
                30,
                showsApplyButton: true,
                groupTitle: "Secrets");
    }
}
