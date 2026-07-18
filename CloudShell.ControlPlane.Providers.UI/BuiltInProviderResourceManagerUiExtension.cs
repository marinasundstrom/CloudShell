using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Providers;
using CloudShell.Hosting.Components.Pages.Resources;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ContainerAppPages = CloudShell.ControlPlane.Providers.UI.ContainerApplication.Pages;
using ConfigurationPages = CloudShell.ControlPlane.Providers.UI.Configuration.Pages;
using DeviceRegistryPages = CloudShell.ControlPlane.Providers.UI.DeviceRegistry.Pages;
using EventBrokerPages = CloudShell.ControlPlane.Providers.UI.EventBroker.Pages;
using RabbitMQPages = CloudShell.ControlPlane.Providers.UI.RabbitMQ.Pages;
using SharedPages = CloudShell.ControlPlane.Providers.UI.Shared.Pages;
using SqlServerPages = CloudShell.ControlPlane.Providers.UI.SqlServer.Pages;
using ResourceManagerResourceClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ControlPlane.Providers.UI;

public sealed class BuiltInProviderResourceManagerUiOptions
{
    public bool EnableHostRunApplicationResourceTypes { get; set; } = true;
}

public sealed class BuiltInProviderResourceManagerUiExtension(
    BuiltInProviderResourceManagerUiOptions? options = null) : ICloudShellExtension
{
    private readonly BuiltInProviderResourceManagerUiOptions _options =
        options ?? new BuiltInProviderResourceManagerUiOptions();

    public CloudShellExtensionManifest Manifest => new(
        "cloudshell.control-plane.providers.resource-manager-ui",
        "Control Plane Providers Resource Manager UI",
        "Adds Resource Manager UI support for built-in provider resource types.",
        "0.1.0",
        [
            "resource-ui.application.executable",
            "resource-ui.application.dotnet-app",
            "resource-ui.application.javascript-app",
            "resource-ui.application.java-app",
            "resource-ui.application.go-app",
            "resource-ui.application.python-app",
            "resource-ui.application.sql-server",
            "resource-ui.application.rabbitmq",
            "resource-ui.application.container-app",
            "resource-ui.service.event-broker",
            "resource-ui.service.device-registry"
        ],
        ["resource-manager.resources"]);

    public void Configure(ICloudShellExtensionBuilder builder)
    {
        builder.Services.TryAddScoped<IResourceDeploymentManager, EmptyResourceDeploymentManager>();
        builder.Services.TryAddScoped<IResourceReplicaSlotStateManager, EmptyResourceReplicaSlotStateManager>();
        builder.Services.TryAddSingleton<IRabbitMQBrokerTopologyProvider, NoopRabbitMQBrokerTopologyProvider>();
        builder.Services.TryAddSingleton<IRabbitMQBrokerDashboardProvider, NoopRabbitMQBrokerDashboardProvider>();

        var resourceTypes = builder;
        if (_options.EnableHostRunApplicationResourceTypes)
        {
            resourceTypes = resourceTypes
                .AddResourceType<SharedPages.RegisterApplicationResource>(
                ExecutableApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Executable application",
                "Inspect executable applications managed through Resource Manager.",
                "application",
                20,
                resourceClass: ResourceManagerResourceClass.Executable)
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ".NET App",
                "Create an application resource from an uploaded .NET application package.",
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
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                "JavaScript App",
                "Create an application resource from an uploaded JavaScript application package.",
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
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                "Java App",
                "Create an application resource from an uploaded Java application package.",
                "application",
                23,
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
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
                "Go App",
                "Create an application resource from an uploaded Go application package.",
                "application",
                24,
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
            .AddResourceType<ApplicationArtifactResourceEditor, ApplicationArtifactResourceEditor>(
                PythonAppResourceTypeProvider.ResourceTypeId.ToString(),
                "Python App",
                "Create an application resource from an uploaded Python application package.",
                "application",
                25,
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
                resourceClass: ResourceManagerResourceClass.Project);
        }

        resourceTypes
            .AddResourceType<ContainerAppPages.RegisterContainerApplicationResource>(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                "Container app",
                "Inspect container applications managed through Resource Manager.",
                "container",
                26,
                probeOptions: new ResourceTypeProbeOptions(SupportsHealth: true),
                resourceClass: ResourceManagerResourceClass.Container)
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                "SQL Server",
                "Inspect SQL Server resources managed through Resource Manager.",
                "database-server",
                27,
                probeOptions: new ResourceTypeProbeOptions(
                    [
                        new ResourceHealthCheck(
                            CreateSqlServerProbeSource(),
                            ResourceProbeType.Liveness,
                            "liveness")
                ]),
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceType<SharedPages.RegisterApplicationResource>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                "RabbitMQ",
                "Inspect RabbitMQ broker resources managed through Resource Manager.",
                "queue",
                28,
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceType<SharedPages.RegisterResource>(
                EventBrokerResourceTypeProvider.ResourceTypeId.ToString(),
                "Event Broker",
                "Inspect event broker resources managed through Resource Manager.",
                "queue",
                29,
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceType<SharedPages.RegisterResource>(
                SqlDatabaseResourceTypeProvider.ResourceTypeId.ToString(),
                "SQL Database",
                "Inspect SQL database child resources managed through Resource Manager.",
                "database-item",
                30,
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceType<SharedPages.RegisterResource>(
                ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(),
                "Configuration Store",
                "Inspect configuration store resources managed through Resource Manager.",
                "settings",
                31,
                resourceClass: ResourceManagerResourceClass.Configuration)
            .AddResourceType<SharedPages.RegisterResource>(
                SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
                "Secrets Vault",
                "Inspect secrets vault resources managed through Resource Manager.",
                "key",
                32,
                resourceClass: ResourceManagerResourceClass.SecretsVault)
            .AddResourceType<SharedPages.RegisterResource>(
                DeviceRegistryResourceTypeProvider.ResourceTypeId.ToString(),
                "Device Registry",
                "Inspect device registry resources managed through Resource Manager.",
                "devices",
                33,
                resourceClass: ResourceManagerResourceClass.Service)
            .AddResourceTab<ConfigurationPages.ConfigurationStoreSettings>(
                ConfigurationStoreResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "settings"),
                "Settings",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "settings")
            .AddResourceTab<ConfigurationPages.SecretsVaultSecrets>(
                SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "secrets"),
                "Secrets",
                20,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "secrets")
            .AddResourceTab<ConfigurationPages.SecretsVaultCertificates>(
                SecretsVaultResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "certificates"),
                "Certificates",
                30,
                showsApplyButton: true,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "certificates")
            .AddResourceTab<DeviceRegistryPages.DeviceRegistryDevices>(
                DeviceRegistryResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "enrolled-devices"),
                "Devices",
                20,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "devices")
            .AddResourceTab<DeviceRegistryPages.DeviceRegistryEnrollmentProfiles>(
                DeviceRegistryResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "enrollment-profiles"),
                "Enrollment profiles",
                30,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "identity")
            .AddResourceTab<EventBrokerPages.EventBrokerStreams>(
                EventBrokerResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.General, "streams"),
                "Streams",
                20,
                groupTitle: ResourceTabGroupTitles.General,
                icon: "queue")
            .AddResourceType<SharedPages.RegisterResource>(
                IdentityProvisioningResourceTypeProvider.ResourceTypeId.ToString(),
                "Identity Provisioning",
                "Inspect identity provisioning resources managed through Resource Manager.",
                "identity",
                34,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                ContainerHostResourceTypeProvider.ResourceTypeId.ToString(),
                "Container Host",
                "Inspect container host resources managed through Resource Manager.",
                "container-host",
                35,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                DockerHostResourceTypeProvider.ResourceTypeId.ToString(),
                "Docker Host",
                "Inspect Docker host resources managed through Resource Manager.",
                "container-host",
                35,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                DockerContainerResourceTypeProvider.ResourceTypeId.ToString(),
                "Docker Container",
                "Inspect Docker container resources managed through Resource Manager.",
                "container",
                36,
                resourceClass: ResourceManagerResourceClass.Container)
            .AddResourceType<SharedPages.RegisterResource>(
                HostConfigurationSourceResourceTypeProvider.ResourceTypeId.ToString(),
                "Host Configuration Source",
                "Inspect host configuration source resources managed through Resource Manager.",
                "settings",
                37,
                resourceClass: ResourceManagerResourceClass.Configuration)
            .AddResourceType<SharedPages.RegisterResource>(
                VirtualNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "Virtual Network",
                "Inspect virtual network resources managed through Resource Manager.",
                "network",
                38,
                resourceClass: ResourceManagerResourceClass.Network)
            .AddResourceType<SharedPages.RegisterResource>(
                LocalHostNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "Local Host Networking",
                "Inspect local host networking resources managed through Resource Manager.",
                "network",
                39,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                MacOSHostNetworkResourceTypeProvider.ResourceTypeId.ToString(),
                "macOS Host Networking",
                "Inspect macOS host networking resources managed through Resource Manager.",
                "network",
                40,
                resourceClass: ResourceManagerResourceClass.Infrastructure)
            .AddResourceType<SharedPages.RegisterResource>(
                LocalVolumeResourceTypeProvider.ResourceTypeId.ToString(),
                "Local Volume",
                "Inspect local volume resources managed through Resource Manager.",
                "storage",
                41,
                resourceClass: ResourceManagerResourceClass.Storage)
            .AddResourceTypeEndpoint(
                AspNetCoreProjectResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http(defaultAssignment: ResourceEndpointAssignment.Auto))
            .AddResourceTypeEndpoint(
                JavaScriptAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http(defaultAssignment: ResourceEndpointAssignment.Auto))
            .AddResourceTypeEndpoint(
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http(defaultAssignment: ResourceEndpointAssignment.Auto))
            .AddResourceTypeEndpoint(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http(defaultAssignment: ResourceEndpointAssignment.Auto))
            .AddResourceTypeEndpoint(
                ContainerApplicationResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http(defaultAssignment: ResourceEndpointAssignment.Auto))
            .AddResourceTypeEndpoint(
                SqlServerResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Tcp("tds", 1433, supportsPortRemapping: false))
            .AddResourceTypeEndpoint(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Tcp("amqp", 5672, supportsPortRemapping: false))
            .AddResourceTypeEndpoint(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                ResourceEndpointDescriptor.Http("management", 15672, supportsPortRemapping: false))
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
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
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
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
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
            .AddResourceTab<SharedPages.ApplicationStorage>(
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
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
                JavaAppResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Endpoints,
                "application.exposure-actions",
                "Application exposure",
                10)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                GoAppResourceTypeProvider.ResourceTypeId.ToString(),
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
            .AddResourceTab<SharedPages.ApplicationConfiguration>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Configuration,
                "Configuration",
                20,
                groupTitle: ResourceTabGroupTitles.General)
            .AddResourceTab<SharedPages.ApplicationEnvironment>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Environment,
                "Environment",
                25,
                groupTitle: ResourceTabGroupTitles.Management)
            .AddResourceTab<SharedPages.ApplicationStorage>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                ResourcePredefinedViewIds.Storage,
                "Storage",
                30,
                groupTitle: ResourceTabGroupTitles.Storage)
            .AddResourceTab<RabbitMQPages.RabbitMQBroker>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Messaging, "broker"),
                "Broker",
                35,
                groupTitle: ResourceTabGroupTitles.Messaging,
                icon: "queue")
            .AddResourceTab<RabbitMQPages.RabbitMQDashboard>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Messaging, "dashboard"),
                "Dashboard",
                37,
                groupTitle: ResourceTabGroupTitles.Messaging,
                icon: "metrics")
            .AddResourceTab<RabbitMQPages.RabbitMQTopology>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
                new ResourceViewId(ResourceTabGroupIds.Messaging, "topology"),
                "Topology",
                40,
                groupTitle: ResourceTabGroupTitles.Messaging,
                icon: "queue")
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
                10)
            .AddResourcePredefinedViewSection<SharedPages.ApplicationEndpointActions>(
                RabbitMQResourceTypeProvider.ResourceTypeId.ToString(),
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

    private sealed class EmptyResourceDeploymentManager : IResourceDeploymentManager
    {
        public Task<IReadOnlyList<ResourceDeploymentRecord>> ListResourceDeploymentsAsync(
            ResourceDeploymentQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceDeploymentRecord>>([]);
    }

    private sealed class EmptyResourceReplicaSlotStateManager : IResourceReplicaSlotStateManager
    {
        public Task<IReadOnlyList<ResourceReplicaSlotState>> ListReplicaSlotStatesAsync(
            ResourceReplicaSlotStateQuery? query = null,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ResourceReplicaSlotState>>([]);
    }
}

public static class BuiltInProviderResourceManagerUiHostExtensions
{
    public static ICloudShellBuilder AddBuiltInProviderResourceManagerUi(
        this ICloudShellBuilder builder,
        Action<BuiltInProviderResourceManagerUiOptions>? configure,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new BuiltInProviderResourceManagerUiOptions();
        configure?.Invoke(options);

        return builder.AddExtension(new BuiltInProviderResourceManagerUiExtension(options), activationPolicy);
    }

    public static ICloudShellBuilder AddBuiltInProviderResourceManagerUi(
        this ICloudShellBuilder builder,
        CloudShellExtensionActivationPolicy activationPolicy = CloudShellExtensionActivationPolicy.Enabled) =>
        AddBuiltInProviderResourceManagerUi(builder, configure: null, activationPolicy);
}
