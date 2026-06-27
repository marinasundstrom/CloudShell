using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

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
    resources
        .AddNetwork("split-sample", isDefault: true)
        .WithDisplayName("Split Sample Network")
        .Persist();

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
