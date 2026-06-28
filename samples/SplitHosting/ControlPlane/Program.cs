using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string graphResourceGroupId = "split-hosting-graph-poc";
const string graphNetworkResourceId = "network:graph-split-sample";

var controlPlane = builder.AddCloudShellControlPlane();
controlPlane.DefineResources(resources =>
{
    resources
        .AddNetwork("graph-split-sample")
        .WithResourceId(graphNetworkResourceId)
        .WithDisplayName("Graph Split Sample Network")
        .WithNetworkKind("Logical")
        .WithHostReadiness("logicalOnly");
});
builder.Services
    .AddNetworkResourceType();
controlPlane.UseResourceGraphIntegration();

controlPlane.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Split Hosting graph POC",
        "Graph-backed resources used to validate remote Control Plane projection.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId)
        .WithResourceGroup(graphResourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
