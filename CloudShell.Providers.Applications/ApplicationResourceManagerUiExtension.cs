using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications.Shared.Pages;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ContainerAppPages = CloudShell.Providers.Applications.ContainerApp.Pages;

namespace CloudShell.Providers.Applications;

public sealed class ApplicationResourceManagerUiExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.applications.resource-manager-ui",
        "Application Resource Manager UI",
        "Adds application resource views for Resource model application resource types without registering the legacy application providers.",
        "0.1.0",
        [
            "resource-ui.application.executable",
            "resource-ui.application.aspnet-core-project",
            "resource-ui.application.sql-server",
            "resource-ui.application.container-app"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<IContainerApplicationHistoryOperations, EmptyContainerApplicationHistoryOperations>();

        builder
            .AddResourceType<RegisterGraphApplicationResource>(
                ApplicationResourceTypes.ExecutableApplication,
                "Executable application",
                "Inspect graph-backed executable applications through Resource Manager.",
                "application",
                20,
                resourceClass: ResourceClass.Executable)
            .AddResourceType<RegisterGraphApplicationResource>(
                ApplicationResourceTypes.AspNetCoreProject,
                "ASP.NET Core project",
                "Inspect graph-backed ASP.NET Core projects through Resource Manager.",
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
            .AddResourceType<ContainerAppPages.RegisterGraphContainerApplicationResource>(
                ApplicationResourceTypes.ContainerApp,
                "Container app",
                "Inspect graph-backed container applications through Resource Manager.",
                "container",
                22,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceClass.Container)
            .AddResourceType<RegisterGraphApplicationResource>(
                ApplicationResourceTypes.SqlServer,
                "SQL Server",
                "Inspect graph-backed SQL Server resources through Resource Manager.",
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
            .AddResourceTab<ContainerAppPages.ApplicationDeployment>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "deployment"),
                "Deployment",
                20,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "deployment")
            .AddResourceTab<ContainerAppPages.ApplicationRevisions>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "revisions"),
                "Revisions",
                25,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "runtime")
            .AddResourceTab<ContainerAppPages.GraphContainerApplicationScaling>(
                ApplicationResourceTypes.ContainerApp,
                new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"),
                "Scale and replicas",
                30,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "scale")
            .AddResourceTab<ContainerAppPages.ApplicationMonitoring>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Monitoring,
                "Monitoring",
                45,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourcePredefinedViewSection<ApplicationEndpointActions>(
                ApplicationResourceTypes.ContainerApp,
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10);
    }

    private sealed class EmptyContainerApplicationHistoryOperations : IContainerApplicationHistoryOperations
    {
        public IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId) => [];

        public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId) => [];
    }
}
