using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.HostVirtualNetwork;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using CloudShell.ResourceModel.ReferenceProviders;
using CloudShell.ResourceModel.ReferenceProviders.ResourceManager;
using CloudShell.ResourceModel.ReferenceProviders.ResourceManager.UI;
using CloudShell.ResourceModel.ResourceManager;
using Microsoft.Extensions.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var targetPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:TargetPort") ?? 5291;
var virtualNetworkPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:VirtualNetworkPort") ?? 5292;
const string resourceGroupId = "host-virtual-network-poc";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Host Virtual Network POC",
    "Resources used by the HostVirtualNetwork sample.");
IResourceDefinitionBuilder hostNetworkingResource = null!;
IResourceDefinitionBuilder apiResource = null!;
cloudShell.DefineResources(resources =>
{
    hostNetworkingResource = resources
        .AddLocalHostNetwork("host-local")
        .WithResourceGroup(resourceGroupId);
    apiResource = resources
        .AddAspNetCoreProject(
            "vnet-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
        .WithDisplayName("VNet API")
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false)
        .WithArguments($"--urls http://localhost:{targetPort}")
        .UseLaunchSettings(false)
        .WithHttpEndpoint(
            host: "localhost",
            port: targetPort);

    var virtualNetwork = resources
        .AddVirtualNetwork("sample-vnet", isDefault: true)
        .WithResourceGroup(resourceGroupId)
        .DependsOn(hostNetworkingResource)
        .DependsOn(apiResource)
        .WithHostReadiness("providerRequired")
        .WithMappingProviders(hostNetworkingResource);
    var publicEndpoint = virtualNetwork
        .AddHttpEndpoint(
            "localhost",
            virtualNetworkPort,
            name: "api-public",
            exposure: "Public");
    virtualNetwork.MapEndpoint(
        publicEndpoint,
        apiResource,
        "http",
        hostNetworkingResource,
        "mapping:api-public",
        "API public ingress");
});
builder.Services
    .AddLocalHostNetworkResourceType()
    .AddVirtualNetworkResourceType()
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();
builder.Services.AddSingleton<
    IVirtualNetworkEndpointMappingReconciler,
    HostVirtualNetworkEndpointMappingReconciler>();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddReferenceProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
