using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using AspNetCoreProjectPages = CloudShell.Providers.Applications.AspNetCoreProject.Pages;
using ContainerAppPages = CloudShell.Providers.Applications.ContainerApp.Pages;
using ExecutableAppPages = CloudShell.Providers.Applications.ExecutableApp.Pages;
using SharedPages = CloudShell.Providers.Applications.Shared.Pages;
using SqlServerPages = CloudShell.Providers.Applications.SqlServer.Pages;

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
        builder.Services.TryAddSingleton<ApplicationResourceDefinitionNormalizer>();
        builder.Services.TryAddSingleton<ApplicationResourceRegistrationService>();
        builder.Services.TryAddSingleton<ApplicationResourceService>();
        builder.Services.TryAddSingleton<IApplicationResourceDefinitionSource>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceProcedureOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceTemplateOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceDeclarationOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceDescriptorOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceActionAvailabilityOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IContainerApplicationResourceProviderOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<ISqlServerApplicationResourceProviderOperations>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IApplicationResourceProjectionSource>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.TryAddSingleton<IResourceVolumeMountMaterializationStore>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationRuntimeStateStore>());
        builder.Services.AddSingleton<IResourceMonitoringProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.AddSingleton<IResourceAppSettingConfigurationProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.AddSingleton<IResourceEnvironmentVariableConfigurationProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.AddSingleton<IHostScopedResourceCleanupProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ApplicationResourceService>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider>(
            serviceProvider => serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ExecutableApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<AspNetCoreProjectResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<ContainerApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourceActionAvailabilityProvider>(
            serviceProvider => serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>());
        builder.Services.AddSingleton<IResourcePermissionGrantStatusProvider>(
            serviceProvider => serviceProvider.GetRequiredService<SqlServerApplicationResourceProvider>());
        builder.Services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IResourceProbeEvaluator, SqlServerResourceProbeEvaluator>());

        builder
            .AddResourceProvider<ExecutableApplicationResourceProvider>()
            .AddResourceProvider<AspNetCoreProjectResourceProvider>()
            .AddResourceProvider<ContainerApplicationResourceProvider>()
            .AddResourceProvider<SqlServerApplicationResourceProvider>()
            .AddLogProvider<ApplicationResourceService>()
            .AddResourceType<ExecutableAppPages.RegisterApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                "Executable application",
                "Register an executable, configure arguments and environment variables, then launch it from CloudShell.",
                "application",
                20,
                resourceClass: ResourceClass.Executable)
            .AddResourceType<AspNetCoreProjectPages.RegisterAspNetCoreProjectResource>(
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
                            Name: "health",
                            Source: ResourceProbeSource.ForHttp("/healthz", "http")),
                        new ResourceHealthCheck(
                            "/alive",
                            ResourceProbeType.Liveness,
                            "http",
                            "liveness",
                            Source: ResourceProbeSource.ForHttp("/alive", "http"))
                    ]),
                resourceClass: ResourceClass.Project)
            .AddResourceType<ContainerAppPages.RegisterContainerImageResource>(
                ApplicationResourceTypes.ContainerApp,
                "Container app",
                "Register a top-level containerized application that runs through the selected or default container host.",
                "container",
                22,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceClass.Container)
            .AddResourceType<SqlServerPages.RegisterSqlServerResource>(
                ApplicationResourceTypes.SqlServer,
                "SQL Server",
                "Register a local SQL Server service with a TDS endpoint for direct access and service discovery.",
                "database-server",
                23,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            ApplicationResourceProbeSources.SqlServer,
                            ResourceProbeType.Liveness,
                            "liveness")
                    ]),
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
            .AddResourceTab<SharedPages.ApplicationOverview>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.UpdateApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ApplicationResourceTypes.ExecutableApplication,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<SharedPages.ApplicationOverview>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.UpdateApplicationResource>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ApplicationResourceTypes.AspNetCoreProject,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<SharedPages.ApplicationOverview>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<ContainerAppPages.ApplicationDeployment>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "deployment"),
                "Deployment",
                20,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "deployment")
            .AddResourceTab<ContainerAppPages.ApplicationScaling>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"),
                "Scale and replicas",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "scale")
            .AddResourceTab<ContainerAppPages.ApplicationMonitoring>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Monitoring,
                "Monitoring",
                45,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.UpdateApplicationResource>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                50,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "storage"),
                "Storage",
                60,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "storage")
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<SharedPages.ApplicationOverview>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Overview,
                "Overview",
                10,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SqlServerPages.UpdateSqlServerResource>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<SqlServerPages.SqlServerDatabases>(
                ApplicationResourceTypes.SqlServer,
                new ResourceViewId(ResourceTabGroupIds.Application, "databases"),
                "Databases",
                35,
                groupTitle: "Data",
                icon: "database-item")
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ApplicationResourceTypes.SqlServer,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10);
    }
}
