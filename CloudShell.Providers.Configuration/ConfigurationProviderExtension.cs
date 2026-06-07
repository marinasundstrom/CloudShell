using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Configuration;

public sealed class ConfigurationProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.configuration",
        "Configuration",
        "Adds local configuration service resources that expose shared settings and secrets to dependent resources.",
        "0.1.0",
        ["resource-type.configuration.store", "resource-trait.environment-variables"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.AddLocalProcessRunner();
        builder.Services.TryAddSingleton<ConfigurationProviderOptions>();
        builder.Services.TryAddSingleton<ConfigurationStore>();
        builder.Services.AddSingleton<IResourceEnvironmentVariableProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ConfigurationResourceProvider>());

        builder
            .AddResourceProvider<ConfigurationResourceProvider>()
            .AddLogProvider<ConfigurationResourceProvider>()
            .AddResourceType<Pages.RegisterConfigurationStore>(
                "configuration.store",
                "Configuration service",
                "Create a local configuration service for settings and secrets that dependent resources can consume.",
                "key",
                15,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            "/healthz",
                            EndpointName: "entries",
                            Name: "health")
                    ]))
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
