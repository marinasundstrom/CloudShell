using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.HostVirtualNetwork;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var targetPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:TargetPort") ?? 5291;
var virtualNetworkPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:VirtualNetworkPort") ?? 5292;
const string resourceGroupId = "host-virtual-network-poc";
const string hostNetworkingResourceId = "networking:host-local";
const string apiResourceId = "application.aspnet-core-project:vnet-api";
const string networkResourceId = "network:sample-vnet";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    var hostNetwork = resources
        .AddLocalHostNetwork("host-local")
        .WithResourceId(hostNetworkingResourceId);
    var api = resources
        .AddAspNetCoreProject(
            "vnet-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
        .WithResourceId(apiResourceId)
        .WithDisplayName("VNet API")
        .WithArguments($"--urls http://localhost:{targetPort}")
        .UseLaunchSettings(false)
        .AddEndpointRequest(
            "http",
            "http",
            host: "localhost",
            port: targetPort,
            exposure: "Local");

    resources
        .AddVirtualNetwork("sample-vnet")
        .WithResourceId(networkResourceId)
        .DependsOn(hostNetwork, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
        .DependsOn(api, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AsDefault()
        .WithHostReadiness("providerRequired")
        .WithMappingProviders(hostNetworkingResourceId)
        .AddEndpoint(
            "api-public",
            "http",
            virtualNetworkPort,
            "Public")
        .AddEndpointNetworkMapping(
            "api-public",
            $"http://localhost:{virtualNetworkPort}",
            name: "API public ingress",
            provider: hostNetwork)
        .MapEndpoint(
            "api-public",
            api,
            "http",
            hostNetwork,
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
cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Host Virtual Network POC",
        "Resources used by the HostVirtualNetwork sample.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, hostNetworkingResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, apiResourceId)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, networkResourceId)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
