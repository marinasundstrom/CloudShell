using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ContainerAppPages = CloudShell.ControlPlane.Providers.UI.ContainerApplication.Pages;
using SharedPages = CloudShell.ControlPlane.Providers.UI.Shared.Pages;
using SqlServerPages = CloudShell.ControlPlane.Providers.UI.SqlServer.Pages;
using ResourceManagerResourceClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ControlPlane.Providers.UI;

public sealed class BuiltInProviderResourceManagerUiExtension : ICloudShellExtension
{
    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.control-plane.providers.resource-manager-ui",
        "Control Plane Providers Resource Manager UI",
        "Adds Resource Manager UI support for built-in Resource model provider resource types.",
        "0.1.0",
        [
            "resource-ui.application.executable",
            "resource-ui.application.aspnet-core-project",
            "resource-ui.application.javascript-app",
            "resource-ui.application.sql-server",
            "resource-ui.application.container-app"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Executable application",
                "Inspect executable applications declared through Resource Manager.",
                "application",
                20,
                resourceClass: ResourceManagerResourceClass.Executable)
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                "ASP.NET Core project",
                "Inspect ASP.NET Core projects declared through Resource Manager.",
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
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                "JavaScript app",
                "Inspect JavaScript and Node.js applications declared through Resource Manager.",
                "javascript",
                22,
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
            .AddResourceType<ContainerAppPages.RegisterContainerApplicationResource>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Container app",
                "Inspect container applications declared through Resource Manager.",
                "container",
                22,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceManagerResourceClass.Container)
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                "SQL Server",
                "Inspect SQL Server resources declared through Resource Manager.",
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
            .AddResourceType<SharedPages.RegisterResource>(
                SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(),
                "SQL Database",
                "Inspect SQL database child resources declared through Resource Manager.",
                "database-item",
                24,
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceType<SharedPages.RegisterResource>(
                ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(),
                "Configuration Store",
                "Inspect configuration store resources declared through Resource Manager.",
                "settings",
                25,
                resourceClass: ResourceManagerResourceClass.Configuration)
            .AddResourceType<SharedPages.RegisterResource>(
                SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
                "Secrets Vault",
                "Inspect secrets vault resources declared through Resource Manager.",
                "key",
                26,
                resourceClass: ResourceManagerResourceClass.SecretsVault)
            .AddResourceType<SharedPages.RegisterResource>(
                IdentityProvisioningResourceTypeProvider.ResourceTypeId.ToString(),
                "Identity Provisioning",
                "Inspect identity provisioning resources declared through Resource Manager.",
                "identity",
                27,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                ContainerHostResourceTypeProvider.ResourceTypeId.ToString(),
                "Container Host",
                "Inspect container host resources declared through Resource Manager.",
                "container-host",
                28,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                DockerHostResourceTypeProvider.ResourceTypeId.ToString(),
                "Docker Host",
                "Inspect Docker host resources declared through Resource Manager.",
                "container-host",
                29,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                DockerContainerResourceTypeProvider.ResourceTypeId.ToString(),
                "Docker Container",
                "Inspect Docker container resources declared through Resource Manager.",
                "container",
                30,
                resourceClass: ResourceManagerResourceClass.Container)
            .AddResourceType<SharedPages.RegisterResource>(
                HostConfigurationSourceResourceTypeProvider.ResourceTypeId.ToString(),
                "Host Configuration Source",
                "Inspect host configuration source resources declared through Resource Manager.",
                "settings",
                31,
                resourceClass: ResourceManagerResourceClass.Configuration)
            .AddResourceType<SharedPages.RegisterResource>(
                VirtualNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "Virtual Network",
                "Inspect virtual network resources declared through Resource Manager.",
                "network",
                32,
                resourceClass: ResourceManagerResourceClass.Network)
            .AddResourceType<SharedPages.RegisterResource>(
                LocalHostNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "Local Host Networking",
                "Inspect local host networking resources declared through Resource Manager.",
                "network",
                33,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                MacOSHostNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "macOS Host Networking",
                "Inspect macOS host networking resources declared through Resource Manager.",
                "network",
                34,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                LocalVolumeResourceTypeProvider.ResourceTypeId.ToString(),
                "Local Volume",
                "Inspect local volume resources declared through Resource Manager.",
                "storage",
                35,
                resourceClass: ResourceManagerResourceClass.Storage)
            .AddResourceTypeEndpoint(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http())
            .AddResourceTypeEndpoint(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Tcp("tds", 1433))
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<ContainerAppPages.ApplicationDeployment>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "deployment"),
                "Deployment",
                20,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "deployment")
            .AddResourceTab<ContainerAppPages.ApplicationRevisions>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "revisions"),
                "Revisions",
                25,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "runtime")
            .AddResourceTab<ContainerAppPages.ContainerApplicationScaling>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "scale-replicas"),
                "Scale and replicas",
                30,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "scale")
            .AddResourceTab<ContainerAppPages.ApplicationMonitoring>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Monitoring,
                "Monitoring",
                45,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                50,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                55,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "storage"),
                "Storage",
                60,
                groupTitle: ResourceTabGroupTitles.Application,
                icon: "storage")
            .AddResourceTab<SharedPages.ApplicationStorage>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SqlServerPages.SqlServerDatabases>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Application, "databases"),
                "Databases",
                35,
                groupTitle: "Data",
                icon: "database-item")
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
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

}

public static class BuiltInProviderResourceManagerUiHostExtensions
{
    public static ICloudShellBuilder AddBuiltInProviderResourceManagerUi(
        this ICloudShellBuilder builder,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddExtension(new BuiltInProviderResourceManagerUiExtension(), activationPolicy);
    }
}
