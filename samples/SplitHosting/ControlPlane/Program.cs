using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "split-hosting-poc";
const string networkResourceId = "network:split-sample";

var controlPlane = builder.AddCloudShellControlPlane();
controlPlane.DefineResources(resources =>
{
    resources
        .AddNetwork("split-sample")
        .WithResourceId(networkResourceId)
        .WithDisplayName("Split Sample Network")
        .WithNetworkKind("Logical")
        .WithHostReadiness("logicalOnly");
});
builder.Services
    .AddNetworkResourceType();
controlPlane.UseResourceGraphIntegration();

controlPlane.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Split Hosting POC",
        "Resources used to validate remote Control Plane projection.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, networkResourceId)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
