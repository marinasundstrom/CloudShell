using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Observability;
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
            "resource-type.application.container-app",
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
        builder.Services.TryAddSingleton<IResourceVolumeMountMaterializationStore>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceMonitoringProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceAppSettingConfigurationProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceEnvironmentVariableConfigurationProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());
        builder.Services.AddSingleton<IHostScopedResourceCleanupProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceProvider>());

        builder
            .AddResourceProvider<ApplicationResourceProvider>()
            .AddLogProvider<ApplicationResourceProvider>()
            .AddResourceType<Pages.RegisterApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                "Executable application",
                "Register an executable, configure arguments and environment variables, then launch it from CloudShell.",
                "application",
                20,
                resourceClass: ResourceClass.Executable)
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
                    ]),
                resourceClass: ResourceClass.Project)
            .AddResourceType<Pages.RegisterContainerImageResource>(
                ApplicationResourceTypes.ContainerApp,
                "Container app",
                "Register a top-level containerized application that runs through the selected or default container host.",
                "container",
                22,
                resourceClass: ResourceClass.Container)
            .AddResourceType<Pages.RegisterSqlServerResource>(
                ApplicationResourceTypes.SqlServer,
                "SQL Server",
                "Register a local SQL Server container with a TDS endpoint for direct access and service discovery.",
                "database",
                23,
                resourceClass: ResourceClass.Service)
            .AddResourceTypeEndpoint(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                ApplicationResourceTypes.ContainerApp,
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                ApplicationResourceTypes.SqlServer,
                ResourceEndpointDescriptor.Tcp("tds", 1433))
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourcePredefinedViewSection<Pages.ApplicationEndpointActions>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourcePredefinedViewSection<Pages.ApplicationEndpointActions>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.ApplicationDeployment>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Runtime, "deployment"),
                "Deployment",
                20,
                groupTitle: ResourceTabGroupTitles.Runtime)
            .AddResourceTab<Pages.ApplicationScaling>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Runtime, "scaling"),
                "Scaling",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Runtime)
            .AddResourceTab<Pages.ApplicationReplicas>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Runtime, "replicas"),
                "Replicas",
                40,
                groupTitle: ResourceTabGroupTitles.Runtime)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                50,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.ApplicationStorage>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Storage,
                "Storage",
                60,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourcePredefinedViewSection<Pages.ApplicationEndpointActions>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<Pages.ApplicationOverview>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.UpdateApplicationResource>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<Pages.ApplicationStorage>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourcePredefinedViewSection<Pages.ApplicationEndpointActions>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10);
    }
}
