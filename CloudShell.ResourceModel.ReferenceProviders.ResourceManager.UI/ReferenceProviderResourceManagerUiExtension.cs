using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceModel.ReferenceProviders;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ContainerAppPages = CloudShell.ResourceModel.ReferenceProviders.ResourceManager.UI.ContainerApplication.Pages;
using SharedPages = CloudShell.ResourceModel.ReferenceProviders.ResourceManager.UI.Shared.Pages;
using SqlServerPages = CloudShell.ResourceModel.ReferenceProviders.ResourceManager.UI.SqlServer.Pages;
using ResourceManagerResourceClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ResourceModel.ReferenceProviders.ResourceManager.UI;

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
