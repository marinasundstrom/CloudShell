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
            "resource-type.application.aspnet-core-project",
            "resource-type.application.container-image",
            "resource-type.application.sql-server",
            "resource-trait.environment-variables",
            "resource-trait.observability"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.AddLocalProcessRunner();
        builder.Services.TryAddSingleton<ApplicationResourceStore>();
        builder.Services.TryAddSingleton<ApplicationRuntimeStateStore>();
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());

        builder
            .AddResourceProvider<ApplicationResourceProvider>()
            .AddLogProvider<ApplicationResourceProvider>()
            .AddResourceType<Pages.RegisterApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                "Executable application",
                "Register an executable, configure arguments and environment variables, then launch it from CloudShell.",
                "application",
                20)
            .AddResourceType<Pages.RegisterAspNetCoreProjectResource>(
                ApplicationResourceTypes.AspNetCoreProject,
                "ASP.NET Core project",
                "Register an ASP.NET Core project and run it through dotnet run with endpoints, references, and service discovery.",
                "web",
                21,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            "/healthz",
                            EndpointName: "http",
                            Name: "health"),
                        new ResourceHealthCheck(
                            "/alive",
                            ResourceProbeType.Liveness,
                            "http",
                            "liveness")
                    ]))
            .AddResourceType<Pages.RegisterContainerImageResource>(
                ApplicationResourceTypes.ContainerImage,
                "Container image",
                "Register a top-level container image resource that runs through the selected or default container engine.",
                "container",
                22)
            .AddResourceType<Pages.RegisterSqlServerResource>(
                ApplicationResourceTypes.SqlServer,
                "SQL Server",
                "Register a local SQL Server container with a TDS endpoint for direct access and service discovery.",
                "database",
                23)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.ExecutableApplication,
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.AspNetCoreProject,
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.AspNetCoreProject,
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.ContainerImage,
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.ContainerImage,
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.SqlServer,
                "overview",
                "Overview",
                10)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.SqlServer,
                "configuration",
                "Configuration",
                20,
                showsApplyButton: true);
    }
}
