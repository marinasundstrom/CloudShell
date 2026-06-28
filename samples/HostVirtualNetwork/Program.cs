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

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
IResourceDefinitionBuilder hostNetworkingResource = null!;
IResourceDefinitionBuilder apiResource = null!;
IResourceDefinitionBuilder networkResource = null!;
cloudShell.DefineResources(resources =>
{
    hostNetworkingResource = resources
        .AddLocalHostNetwork("host-local");
    apiResource = resources
        .AddAspNetCoreProject(
            "vnet-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
        .WithDisplayName("VNet API")
        .WithArguments($"--urls http://localhost:{targetPort}")
        .UseLaunchSettings(false)
        .WithHttpEndpoint(
            host: "localhost",
            port: targetPort);

    networkResource = resources
        .AddVirtualNetwork("sample-vnet")
        .DependsOn(hostNetworkingResource, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
        .DependsOn(apiResource, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AsDefault()
        .WithHostReadiness("providerRequired")
        .WithMappingProviders(hostNetworkingResource.EffectiveResourceId)
        .AddEndpoint(
            "api-public",
            "http",
            virtualNetworkPort,
            "Public")
        .AddEndpointNetworkMapping(
            "api-public",
            $"http://localhost:{virtualNetworkPort}",
            name: "API public ingress",
            provider: hostNetworkingResource)
        .MapEndpoint(
            "api-public",
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
cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Host Virtual Network POC",
        "Resources used by the HostVirtualNetwork sample.");

    resources
        .Declare(hostNetworkingResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(apiResource)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(networkResource)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
