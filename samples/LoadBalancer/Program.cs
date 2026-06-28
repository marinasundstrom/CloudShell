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
using CloudShell.Providers.Traefik;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection.Extensions;

var builder = CloudShellApplication.CreateBuilder(args);

const string dockerHostResourceId = "docker:sample-host";
const string webResourceId = "application.container-app:web";
const string apiResourceId = "application.container-app:api";
const string postgresResourceId = "application.container-app:postgres";
const string loadBalancerResourceId = "load-balancer:public";
const string dnsZoneResourceId = "dns:cloudshell-local";
const string appNameMappingResourceId = "dns:cloudshell-local:name:app-cloudshell-local";
const string apiNameMappingResourceId = "dns:cloudshell-local:name:api-cloudshell-local";
const string resourceGroupId = "load-balancer-poc";
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
    var dockerHost = resources
        .AddDockerHost("sample-host")
        .WithResourceId(dockerHostResourceId);
    var web = resources
        .AddContainerApplication("web")
        .WithResourceId(webResourceId)
        .WithDisplayName("Web App")
        .UseDockerHost(dockerHost)
        .WithImage("nginx:1.27-alpine");
    var api = resources
        .AddContainerApplication("api")
        .WithResourceId(apiResourceId)
        .WithDisplayName("API Service")
        .UseDockerHost(dockerHost)
        .WithImage("traefik/whoami:v1.10")
        .WithReplicas(3);
    var postgres = resources
        .AddContainerApplication("postgres")
        .WithResourceId(postgresResourceId)
        .WithDisplayName("Postgres Replica Set")
        .UseDockerHost(dockerHost)
        .WithImage("postgres:16-alpine");

    resources
        .AddLoadBalancer("public")
        .WithResourceId(loadBalancerResourceId)
        .WithDisplayName("Public Load Balancer")
        .WithProvider("traefik")
        .UseHost(dockerHost)
        .ExposeHttp()
        .ExposeHttps()
        .ExposeTcp(5432)
        .MapHost("app.cloudshell.local", web, port: 80)
        .MapPath("api.cloudshell.local", "/v1", api, port: 80)
        .MapTcp(5432, postgres, targetPort: 5432);

    var dnsZone = resources
        .AddDnsZone("cloudshell-local")
        .WithResourceId(dnsZoneResourceId)
        .WithDisplayName("CloudShell Local DNS")
        .WithZoneName("cloudshell.local")
        .WithProvider("local-hostnames");
    resources
        .AddNameMapping("app-cloudshell-local")
        .WithResourceId(appNameMappingResourceId)
        .WithDisplayName("app.cloudshell.local")
        .WithHostName("app.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(dnsZone)
        .MapsTarget(loadBalancerResourceId, LoadBalancerResourceTypeProvider.ResourceTypeId);
    resources
        .AddNameMapping("api-cloudshell-local")
        .WithResourceId(apiNameMappingResourceId)
        .WithDisplayName("api.cloudshell.local")
        .WithHostName("api.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(dnsZone)
        .MapsTarget(loadBalancerResourceId, LoadBalancerResourceTypeProvider.ResourceTypeId);
});
builder.Services
    .AddLocalContainerApplicationResourceTypes()
    .AddLoadBalancerResourceType()
    .AddDnsZoneResourceType()
    .AddNameMappingResourceType();
cloudShell.UseResourceGraphIntegration();
builder.Services.Replace(
    ServiceDescriptor.Singleton<ILoadBalancerConfigurationApplier, LoadBalancerGraphTraefikConfigurationApplier>());
builder.Services.Replace(
    ServiceDescriptor.Singleton<IDnsZoneNameMappingReconciler, LoadBalancerGraphNameMappingReconciler>());

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

cloudShell.AddApplicationResourceManagerUi();

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
        resourceGroupId,
        "Load Balancer POC",
        "Resource model resources used by the LoadBalancer sample.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, dockerHostResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, webResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, apiResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, postgresResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, loadBalancerResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, dnsZoneResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, appNameMappingResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, apiNameMappingResourceId)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
