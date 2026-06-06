using CloudShell.Abstractions.Extensions;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.applications",
        "Applications",
        "Adds executable application resources with process lifecycle, logs, and environment variables.",
        "0.1.0",
        ["resource-type.application.executable", "resource-trait.environment-variables"],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<ApplicationResourceStore>();

        builder
            .AddResourceProvider<ApplicationResourceProvider>()
            .AddLogProvider<ApplicationResourceProvider>()
            .AddResourceType<Pages.RegisterApplicationResource>(
                "application.executable",
                "Executable application",
                "Register an executable, configure arguments and environment variables, then launch it from CloudShell.",
                "application",
                20)
            .AddResourceTab<Pages.ApplicationOverview>(
                "application.executable",
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                "application.executable",
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true);
    }
}
