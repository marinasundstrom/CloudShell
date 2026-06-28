using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Applications.Shared.Pages;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ApplicationContainerAppPages = CloudShell.Providers.Applications.ContainerApp.Pages;
using GraphContainerAppPages = CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI.ContainerApplication.Pages;
using GraphSharedPages = CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI.Shared.Pages;
using ResourceManagerResourceClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI;

public sealed class ReferenceProviderResourceManagerUiExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.reference-providers.resource-manager-ui",
        "Reference Provider Resource Manager UI",
        "Adds Resource Manager UI support for Resource model reference provider resource types.",
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
            .AddResourceType<GraphSharedPages.RegisterGraphApplicationResource>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Executable application",
                "Inspect Resource graph executable applications through Resource Manager.",
                "application",
                20,
                resourceClass: ResourceManagerResourceClass.Executable)
            .AddResourceType<GraphSharedPages.RegisterGraphApplicationResource>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                "ASP.NET Core project",
                "Inspect Resource graph ASP.NET Core projects through Resource Manager.",
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
                resourceClass: ResourceManagerResourceClass.Project)
            .AddResourceType<GraphContainerAppPages.RegisterGraphContainerApplicationResource>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Container app",
                "Inspect Resource graph container applications through Resource Manager.",
                "container",
                22,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceManagerResourceClass.Container)
            .AddResourceType<GraphSharedPages.RegisterGraphApplicationResource>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                "SQL Server",
                "Inspect Resource graph SQL Server resources through Resource Manager.",
                "database-server",
                23,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            CreateSqlServerProbeSource(),
                            ResourceProbeType.Liveness,
                            "liveness")
                    ]),
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceTypeEndpoint(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Tcp("tds", 1433))
            .AddResourceTab<ApplicationContainerAppPages.ApplicationDeployment>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "deployment"),
                "Deployment",
                20,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "deployment")
            .AddResourceTab<ApplicationContainerAppPages.ApplicationRevisions>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "revisions"),
                "Revisions",
                25,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "runtime")
            .AddResourceTab<GraphContainerAppPages.GraphContainerApplicationScaling>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"),
                "Scale and replicas",
                30,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "scale")
            .AddResourceTab<ApplicationContainerAppPages.ApplicationMonitoring>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Monitoring,
                "Monitoring",
                45,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourcePredefinedViewSection<ApplicationEndpointActions>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10);
    }

    private static ResourceProbeSource CreateSqlServerProbeSource() =>
        new(
            "application.sql-server",
            Metadata: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["endpoint"] = "tds",
                ["database"] = "master"
            });

    private sealed class EmptyContainerApplicationHistoryOperations : IContainerApplicationHistoryOperations
    {
        public IReadOnlyList<ApplicationContainerDeployment> GetContainerDeployments(string applicationId) => [];

        public IReadOnlyList<ApplicationContainerRevisionHistoryEntry> GetContainerRevisions(string applicationId) => [];
    }
}

public static class ReferenceProviderResourceManagerUiHostExtensions
{
    public static ICloudShellBuilder AddReferenceProviderResourceManagerUi(
        this ICloudShellBuilder builder,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddExtension(new ReferenceProviderResourceManagerUiExtension(), activationPolicy);
    }
}
