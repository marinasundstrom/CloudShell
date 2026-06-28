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
IResourceDefinitionBuilder dockerHostResource = null!;
IResourceDefinitionBuilder webResource = null!;
IResourceDefinitionBuilder apiResource = null!;
IResourceDefinitionBuilder postgresResource = null!;
IResourceDefinitionBuilder loadBalancerResource = null!;
IResourceDefinitionBuilder dnsZoneResource = null!;
IResourceDefinitionBuilder appNameMappingResource = null!;
IResourceDefinitionBuilder apiNameMappingResource = null!;
cloudShell.DefineResources(resources =>
{
    dockerHostResource = resources
        .AddDockerHost("sample-host");
    webResource = resources
        .AddContainerApplication("web")
        .WithDisplayName("Web App")
        .UseDockerHost(dockerHostResource)
        .WithImage("nginx:1.27-alpine");
    apiResource = resources
        .AddContainerApplication("api")
        .WithDisplayName("API Service")
        .UseDockerHost(dockerHostResource)
        .WithImage("traefik/whoami:v1.10")
        .WithReplicas(3);
    postgresResource = resources
        .AddContainerApplication("postgres")
        .WithDisplayName("Postgres Replica Set")
        .UseDockerHost(dockerHostResource)
        .WithImage("postgres:16-alpine");

    loadBalancerResource = resources
        .AddLoadBalancer("public")
        .WithDisplayName("Public Load Balancer")
        .WithProvider("traefik")
        .UseHost(dockerHostResource)
        .ExposeHttp()
        .ExposeHttps()
        .ExposeTcp(5432)
        .MapHost("app.cloudshell.local", webResource, port: 80)
        .MapPath("api.cloudshell.local", "/v1", apiResource, port: 80)
        .MapTcp(5432, postgresResource, targetPort: 5432);

    dnsZoneResource = resources
        .AddDnsZone("cloudshell-local")
        .WithDisplayName("CloudShell Local DNS")
        .WithZoneName("cloudshell.local")
        .WithProvider("local-hostnames");
    appNameMappingResource = resources
        .AddNameMapping("app-cloudshell-local")
        .WithDisplayName("app.cloudshell.local")
        .WithHostName("app.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(dnsZoneResource)
        .MapsTarget(loadBalancerResource, LoadBalancerResourceTypeProvider.ResourceTypeId);
    apiNameMappingResource = resources
        .AddNameMapping("api-cloudshell-local")
        .WithDisplayName("api.cloudshell.local")
        .WithHostName("api.cloudshell.local")
        .WithTargetEndpointName("http")
        .InDnsZone(dnsZoneResource)
        .MapsTarget(loadBalancerResource, LoadBalancerResourceTypeProvider.ResourceTypeId);
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
        .Declare(dockerHostResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(webResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(apiResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(postgresResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(loadBalancerResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(dnsZoneResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(appNameMappingResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(apiNameMappingResource)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
