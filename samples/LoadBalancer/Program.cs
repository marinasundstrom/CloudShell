using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

const string graphDockerHostResourceId = "docker:graph-sample-host";
const string graphWebResourceId = "application.container-app:graph-web";
const string graphApiResourceId = "application.container-app:graph-api";
const string graphPostgresResourceId = "application.container-app:graph-postgres";
const string graphLoadBalancerResourceId = "load-balancer:graph-public";
const string graphResourceGroupId = "load-balancer-graph-poc";
var dynamicConfigurationDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "traefik");
var localHostsFilePath = Environment.GetEnvironmentVariable("CLOUDSHELL_LOCAL_HOSTS_FILE");
if (!string.IsNullOrWhiteSpace(localHostsFilePath))
{
    builder.Services
        .GetOrAddPlatformResourceOptions()
        .LocalHostNameHostsFilePath = localHostsFilePath;
}

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-sample-host",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ResourceId: graphDockerHostResourceId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId),
        new ResourceGraphState(
            "graph-web",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: graphWebResourceId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DisplayName: "Graph Web App",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerHostResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "nginx:1.27-alpine"
            }),
        new ResourceGraphState(
            "graph-api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: graphApiResourceId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DisplayName: "Graph API Service",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerHostResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "traefik/whoami:v1.10",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] =
                    3
            }),
        new ResourceGraphState(
            "graph-postgres",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: graphPostgresResourceId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DisplayName: "Graph Postgres Replica Set",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerHostResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    "postgres:16-alpine"
            }),
        new ResourceGraphState(
            "graph-public",
            LoadBalancerResourceTypeProvider.ResourceTypeId,
            ResourceId: graphLoadBalancerResourceId,
            ProviderId: LoadBalancerResourceTypeProvider.ProviderId,
            DisplayName: "Graph Public Load Balancer",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerHostResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphWebResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphApiResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphPostgresResourceId,
                    typeId: ContainerApplicationResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [LoadBalancerResourceTypeProvider.Attributes.Provider] = "traefik",
                [LoadBalancerResourceTypeProvider.Attributes.HostResourceId] =
                    graphDockerHostResourceId,
                [LoadBalancerResourceTypeProvider.Attributes.EntrypointCount] = 3,
                [LoadBalancerResourceTypeProvider.Attributes.RouteCount] = 3,
                [LoadBalancerResourceTypeProvider.Attributes.HttpRouteCount] = 2,
                [LoadBalancerResourceTypeProvider.Attributes.TcpRouteCount] = 1
            })
    ])
    .AddDockerHostResourceType()
    .AddContainerApplicationResourceType()
    .AddLoadBalancerResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddDockerProvider()
    .AddTraefikProvider(options =>
    {
        options.DynamicConfigurationDirectory = dynamicConfigurationDirectory;
        options.ManageRuntimeContainer = !string.Equals(
            Environment.GetEnvironmentVariable("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    });

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Load Balancer graph POC",
        "Side-by-side graph-backed resources used while porting the LoadBalancer sample.");

    var dockerHost = resources
        .AddDocker("sample-host")
        .Persist(overwrite: true);

    var webApp = resources
        .AddContainerApplication(
            "web",
            "nginx:1.27-alpine")
        .WithEndpoint(
            "http",
            targetPort: 80,
            port: 5080,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var apiService = resources
        .AddContainerApplication(
            "api",
            "traefik/whoami:v1.10")
        .WithEndpoint(
            "http",
            targetPort: 80,
            port: 5081,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithReplicas(3)
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var postgres = resources
        .AddContainerApplication(
            "postgres",
            "postgres:16-alpine")
        .WithEndpoint(
            "postgres",
            targetPort: 5432,
            port: 55432,
            protocol: "tcp",
            exposure: ResourceExposureScope.Local)
        .WithEnvironment("POSTGRES_PASSWORD", "cloudshell")
        .WithEnvironment("POSTGRES_DB", "cloudshell")
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDockerHostResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphWebResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphPostgresResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphLoadBalancerResourceId)
        .WithResourceGroup(graphResourceGroupId);

    var lb = resources
        .AddLoadBalancer("public")
        .UseProvider("traefik")
        .UseContainerHost(dockerHost)
        .ExposeHttp(80)
        .ExposeHttps(443)
        .ExposeTcp(5432);

    lb.MapHost("app.cloudshell.local", webApp, port: 80);
    lb.MapPath("api.cloudshell.local", "/v1", apiService, port: 80);
    lb.MapTcp(5432, postgres, targetPort: 5432);

    resources
        .AddDnsZone("cloudshell-local", zoneName: "cloudshell.local")
        .UseLocalHostNames()
        .MapHost("app.cloudshell.local", lb, "http")
        .MapHost("api.cloudshell.local", lb, "http");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
