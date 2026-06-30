using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.HostVirtualNetwork;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using Microsoft.Extensions.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var targetPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:TargetPort") ?? 5291;
var workerTargetPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:WorkerTargetPort") ?? 5293;
var virtualNetworkPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:VirtualNetworkPort") ?? 5292;
var coreDnsDirectory = builder.Configuration.GetValue<string?>("HostVirtualNetwork:CoreDnsDirectory");
const string resourceGroupId = "host-virtual-network";

var cloudShell = builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Host Virtual Network",
    "Resources used by the HostVirtualNetwork sample.");
IResourceDefinitionBuilder hostNetworkingResource = null!;
IResourceDefinitionBuilder apiResource = null!;
IResourceDefinitionBuilder workerResource = null!;
cloudShell.DefineResources(resources =>
{
    hostNetworkingResource = resources
        .AddLocalHostNetwork("host-local")
        .WithResourceGroup(resourceGroupId);
    var virtualNetwork = resources
        .AddVirtualNetwork("sample-vnet", isDefault: true)
        .WithResourceGroup(resourceGroupId)
        .DependsOn(hostNetworkingResource)
        .WithHostReadiness("providerRequired")
        .WithMappingProviders(hostNetworkingResource);
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
            port: targetPort)
        .WithHttpEndpoint(
            name: "vnet-http",
            port: 80,
            targetPort: 80,
            exposure: "Network",
            ipAddress: "10.42.0.10",
            network: virtualNetwork,
            assignment: "Manual");
    workerResource = resources
        .AddAspNetCoreProject(
            "vnet-worker",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
        .WithDisplayName("VNet Worker")
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false)
        .WithArguments($"--urls http://localhost:{workerTargetPort}")
        .UseLaunchSettings(false)
        .WithHttpEndpoint(
            host: "localhost",
            port: workerTargetPort)
        .WithHttpEndpoint(
            name: "vnet-http",
            port: 80,
            targetPort: 80,
            exposure: "Network",
            ipAddress: "10.42.0.11",
            network: virtualNetwork,
            assignment: "Manual");
    virtualNetwork
        .DependsOn(apiResource)
        .DependsOn(workerResource);
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
    resources
        .AddDnsZone("sample-vnet-internal", zoneName: "internal.cloudshell.test")
        .WithDisplayName("Sample VNet Internal DNS")
        .WithResourceGroup(resourceGroupId)
        .WithProvider(CoreDnsZoneFilePublishingProvider.ProviderNameValue)
        .MapHost(
            "api.internal.cloudshell.test",
            apiResource,
            endpointName: "vnet-http",
            name: "api-internal",
            exposure: "Private",
            configure: mapping => mapping.WithResourceGroup(resourceGroupId))
        .MapHost(
            "worker.internal.cloudshell.test",
            workerResource,
            endpointName: "vnet-http",
            name: "worker-internal",
            exposure: "Private",
            configure: mapping => mapping.WithResourceGroup(resourceGroupId));
});
cloudShell.UseBuiltInResourceModelProviders(options =>
{
    options.IncludeDefaultEnvironmentResources = false;
});
builder.Services.AddCoreDnsZoneFilePublishingProvider(options =>
{
    options.OutputDirectory = string.IsNullOrWhiteSpace(coreDnsDirectory)
        ? Path.Combine(builder.Environment.ContentRootPath, "Data", "coredns")
        : coreDnsDirectory;
});

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddBuiltInProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
