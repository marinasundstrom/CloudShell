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
        "Adds application resource views for graph-backed application resource types without registering the legacy application providers.",
        "0.1.0",
        [
            "resource-ui.application.container-app"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddSingleton<IContainerApplicationHistoryOperations, EmptyContainerApplicationHistoryOperations>();

        builder
            .AddResourceType<ContainerAppPages.RegisterGraphContainerApplicationResource>(
                ApplicationResourceTypes.ContainerApp,
                "Container app",
                "Inspect graph-backed container applications through Resource Manager.",
                "container",
                22,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceClass.Container)
            .AddResourceTypeEndpoint(
                ApplicationResourceTypes.ContainerApp,
                ResourceEndpointDescriptor.Http())
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
