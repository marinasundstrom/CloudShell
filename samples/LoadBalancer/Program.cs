using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Traefik;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "load-balancer";
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

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var resourceGroup = resources.AddResourceGroup(
                resourceGroupId,
                "Load Balancer",
                "Resource model resources used by the LoadBalancer sample.");

            var dockerHostResource = resources
                .AddDockerHost("sample-host")
                .WithResourceGroup(resourceGroup);
            var webResource = resources
                .AddContainerApplication("web")
                .WithDisplayName("Web App")
                .WithResourceGroup(resourceGroup)
                .UseDockerHost(dockerHostResource)
                .WithImage("nginx:1.27-alpine");
            var apiResource = resources
                .AddContainerApplication("api")
                .WithDisplayName("API Service")
                .WithResourceGroup(resourceGroup)
                .UseDockerHost(dockerHostResource)
                .WithImage("traefik/whoami:v1.10")
                .WithReplicas(3);
            var postgresResource = resources
                .AddContainerApplication("postgres")
                .WithDisplayName("Postgres Replica Set")
                .WithResourceGroup(resourceGroup)
                .UseDockerHost(dockerHostResource)
                .WithImage("postgres:16-alpine");

            var loadBalancerResource = resources
                .AddLoadBalancer("public")
                .WithDisplayName("Public Load Balancer")
                .WithResourceGroup(resourceGroup)
                .WithProvider("traefik")
                .UseHost(dockerHostResource)
                .ExposeHttp()
                .ExposeHttps()
                .ExposeTcp(5432)
                .MapHost("app.cloudshell.local", webResource, port: 80)
                .MapPath("api.cloudshell.local", "/v1", apiResource, port: 80)
                .MapTcp(5432, postgresResource, targetPort: 5432);

            resources
                .AddDnsZone("cloudshell-local", zoneName: "cloudshell.local")
                .WithDisplayName("CloudShell Local DNS")
                .WithResourceGroup(resourceGroup)
                .UseLocalHostNames()
                .MapHost(
                    "app.cloudshell.local",
                    loadBalancerResource,
                    endpointName: "http",
                    name: "app-cloudshell-local",
                    configure: mapping => mapping.WithResourceGroup(resourceGroup))
                .MapHost(
                    "api.cloudshell.local",
                    loadBalancerResource,
                    endpointName: "http",
                    name: "api-cloudshell-local",
                    configure: mapping => mapping.WithResourceGroup(resourceGroup));
        });
    });
builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

cloudShell
    .AddTraefikProvider(options =>
    {
        options.DynamicConfigurationDirectory = dynamicConfigurationDirectory;
        options.ManageRuntimeContainer = !string.Equals(
            Environment.GetEnvironmentVariable("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME"),
            "true",
            StringComparison.OrdinalIgnoreCase);
    });

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
