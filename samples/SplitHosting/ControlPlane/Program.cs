using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "split-hosting-poc";

var controlPlane = builder.AddCloudShellControlPlane();
IResourceDefinitionBuilder networkResource = null!;
controlPlane.DefineResources(resources =>
{
    networkResource = resources
        .AddNetwork("split-sample")
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
        .Declare(networkResource)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
