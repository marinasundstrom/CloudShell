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
var virtualNetworkPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:VirtualNetworkPort") ?? 5290;
var graphVirtualNetworkPort = builder.Configuration.GetValue<int?>("HostVirtualNetwork:GraphVirtualNetworkPort") ?? 5292;
var graphOnly = builder.Configuration.GetValue("HostVirtualNetwork:GraphOnly", true);
const string graphResourceGroupId = "host-virtual-network-graph-poc";
const string graphHostNetworkingResourceId = "networking:graph-host-local";
const string graphApiResourceId = "application.aspnet-core-project:graph-vnet-api";
const string graphNetworkResourceId = "network:graph-sample-vnet";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    var graphHostNetwork = resources
        .AddLocalHostNetwork("graph-host-local")
        .WithResourceId(graphHostNetworkingResourceId);
    var graphApi = resources
        .AddAspNetCoreProject(
            "graph-vnet-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj")
        .WithResourceId(graphApiResourceId)
        .WithDisplayName("Graph VNet API")
        .WithArguments($"--urls http://localhost:{targetPort}")
        .UseLaunchSettings(false)
        .AddEndpointRequest(
            "http",
            "http",
            host: "localhost",
            port: targetPort,
            exposure: "Local");

    resources
        .AddVirtualNetwork("graph-sample-vnet")
        .WithResourceId(graphNetworkResourceId)
        .DependsOn(graphHostNetwork, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
        .DependsOn(graphApi, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AsDefault()
        .WithHostReadiness("providerRequired")
        .WithMappingProviders(graphHostNetworkingResourceId)
        .AddEndpoint(
            "api-public",
            "http",
            graphVirtualNetworkPort,
            "Public")
        .AddEndpointNetworkMapping(
            "api-public",
            $"http://localhost:{graphVirtualNetworkPort}",
            name: "Graph API public ingress",
            provider: graphHostNetwork)
        .MapEndpoint(
            "api-public",
            graphApi,
            "http",
            graphHostNetwork,
            "mapping:graph-api-public",
            "Graph API public ingress");
});
builder.Services
    .AddLocalHostNetworkResourceType()
    .AddVirtualNetworkResourceType()
    .AddAspNetCoreProjectResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");
builder.Services.AddSingleton<
    IVirtualNetworkEndpointMappingReconciler,
    HostVirtualNetworkGraphEndpointMappingReconciler>();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell.AddApplicationProvider();
}

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Host Virtual Network graph POC",
        "Side-by-side graph-backed resources used while porting the HostVirtualNetwork sample.");

    if (!graphOnly)
    {
        var hostNetworking = resources.AddLocalHostNetworking();

        var api = resources
            .AddAspNetCoreProject(
                "vnet-api",
                "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
                endpoint: $"http://localhost:{targetPort}")
            .WithAutoStart(false);

        var network = resources
            .AddVirtualNetwork(
                "sample-vnet",
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
    }

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphHostNetworkingResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithResourceGroup(graphResourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId)
        .WithResourceGroup(graphResourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
