using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.LoadBalancer;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = CloudShellApplication.CreateBuilder(args);

const string graphDockerHostResourceId = "docker:graph-sample-host";
const string graphWebResourceId = "application.container-app:graph-web";
const string graphApiResourceId = "application.container-app:graph-api";
const string graphPostgresResourceId = "application.container-app:graph-postgres";
const string graphLoadBalancerResourceId = "load-balancer:graph-public";
const string graphDnsZoneResourceId = "dns:graph-cloudshell-local";
const string graphAppNameMappingResourceId = "dns:graph-cloudshell-local:name:app-cloudshell-local";
const string graphApiNameMappingResourceId = "dns:graph-cloudshell-local:name:api-cloudshell-local";
const string graphResourceGroupId = "load-balancer-graph-poc";
var graphOnly = builder.Configuration.GetValue("LoadBalancer:GraphOnly", true);
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
cloudShell.DefineResources(resources =>
{
    var graphDockerHost = resources
        .AddDockerHost("graph-sample-host")
        .WithResourceId(graphDockerHostResourceId);
    var graphWeb = resources
        .AddContainerApplication("graph-web")
        .WithResourceId(graphWebResourceId)
        .WithDisplayName("Graph Web App")
        .UseDockerHost(graphDockerHost)
        .WithImage("nginx:1.27-alpine");
    var graphApi = resources
        .AddContainerApplication("graph-api")
        .WithResourceId(graphApiResourceId)
        .WithDisplayName("Graph API Service")
        .UseDockerHost(graphDockerHost)
        .WithImage("traefik/whoami:v1.10")
        .WithReplicas(3);
    var graphPostgres = resources
        .AddContainerApplication("graph-postgres")
        .WithResourceId(graphPostgresResourceId)
        .WithDisplayName("Graph Postgres Replica Set")
        .UseDockerHost(graphDockerHost)
        .WithImage("postgres:16-alpine");

    resources
        .AddLoadBalancer("graph-public")
        .WithResourceId(graphLoadBalancerResourceId)
        .WithDisplayName("Graph Public Load Balancer")
        .WithProvider("traefik")
        .UseHost(graphDockerHost)
        .ExposeHttp()
        .ExposeHttps()
        .ExposeTcp(5432)
        .MapHost("app.cloudshell.local", graphWeb, port: 80)
        .MapPath("api.cloudshell.local", "/v1", graphApi, port: 80)
        .MapTcp(5432, graphPostgres, targetPort: 5432);

    var graphDnsZone = resources
        .AddDnsZone("graph-cloudshell-local")
        .WithResourceId(graphDnsZoneResourceId)
        .WithDisplayName("Graph CloudShell Local DNS")
        .WithZoneName("cloudshell.local")
        .WithProvider("local-hostnames");
    resources
        .AddNameMapping("graph-app-cloudshell-local")
        .WithResourceId(graphAppNameMappingResourceId)
        .WithDisplayName("Graph app.cloudshell.local")
        .WithHostName("app.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(graphDnsZone)
        .MapsTarget(graphLoadBalancerResourceId, LoadBalancerResourceTypeProvider.ResourceTypeId);
    resources
        .AddNameMapping("graph-api-cloudshell-local")
        .WithResourceId(graphApiNameMappingResourceId)
        .WithDisplayName("Graph api.cloudshell.local")
        .WithHostName("api.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(graphDnsZone)
        .MapsTarget(graphLoadBalancerResourceId, LoadBalancerResourceTypeProvider.ResourceTypeId);
});
builder.Services
    .AddDockerHostResourceType()
    .AddContainerApplicationResourceType()
    .AddLoadBalancerResourceType()
    .AddDnsZoneResourceType()
    .AddNameMappingResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");
builder.Services.Replace(
    ServiceDescriptor.Singleton<ILoadBalancerConfigurationApplier, LoadBalancerGraphTraefikConfigurationApplier>());
builder.Services.Replace(
    ServiceDescriptor.Singleton<IDnsZoneNameMappingReconciler, LoadBalancerGraphNameMappingReconciler>());

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell
        .AddApplicationProvider()
        .AddDockerProvider();
}

cloudShell
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
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDnsZoneResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphAppNameMappingResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiNameMappingResourceId)
        .WithResourceGroup(graphResourceGroupId);

    if (!graphOnly)
    {
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
    }
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
