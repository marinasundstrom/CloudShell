using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var graphOnly = builder.Configuration.GetValue("SplitHosting:GraphOnly", true);
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
    .AddNetworkResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

controlPlane.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Split Hosting graph POC",
        "Graph-backed resources used to validate remote Control Plane projection.");

    if (!graphOnly)
    {
        resources
            .AddNetwork("split-sample", isDefault: true)
            .WithDisplayName("Split Sample Network")
            .Persist();
    }

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId)
        .WithResourceGroup(graphResourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
