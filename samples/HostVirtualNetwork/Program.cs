using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;

var builder = WebApplication.CreateBuilder(args);

const int targetPort = 5291;
const int virtualNetworkPort = 5290;

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider();

cloudShell.Resources(resources =>
{
    var hostNetworking = resources.AddMacOSHostNetworking();

    var api = resources
        .AddAspNetCoreProject(
            "application:vnet-api",
            "Virtual Network API",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            endpoint: $"http://localhost:{targetPort}")
        .WithAutoStart(false);

    var network = resources
        .AddVirtualNetwork(
            "network:sample-vnet",
            "Sample Virtual Network",
            isDefault: true);

    var ingress = network.AddHttpEndpoint(
        "localhost",
        virtualNetworkPort,
        "api-public",
        ResourceExposureScope.Public);

    network.MapEndpoint(
        ingress,
        new ResourceEndpointReference(api.ResourceId, "http"),
        hostNetworking,
        "mapping:api-public",
        "API public ingress");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
