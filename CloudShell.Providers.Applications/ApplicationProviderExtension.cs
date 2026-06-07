using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationProviderExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.applications",
        "Applications",
        "Adds executable application resources with process lifecycle, logs, and environment variables.",
        "0.1.0",
        [
            "resource-type.application.executable",
            "resource-type.application.container-image",
            "resource-type.application.sql-server",
            "resource-trait.environment-variables"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<ApplicationResourceStore>();
        builder.Services.TryAddSingleton<ApplicationRuntimeStateStore>();
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());

        builder
            .AddResourceProvider<ApplicationResourceProvider>()
            .AddLogProvider<ApplicationResourceProvider>()
            .AddResourceType<Pages.RegisterApplicationResource>(
                "application.executable",
                "Executable application",
                "Register an executable, configure arguments and environment variables, then launch it from CloudShell.",
                "application",
                20)
            .AddResourceType<Pages.RegisterContainerImageResource>(
                "application.container-image",
                "Container image",
                "Register a top-level container image resource that runs through the selected or default container engine.",
                "container",
                21)
            .AddResourceType<Pages.RegisterSqlServerResource>(
                "application.sql-server",
                "SQL Server",
                "Register a local SQL Server container with a TDS endpoint for direct access and service discovery.",
                "database",
                22)
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
