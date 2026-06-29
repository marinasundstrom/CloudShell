using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "split-hosting-poc";

var controlPlane = builder.AddCloudShellControlPlane();
controlPlane.AddResourceGroup(
    resourceGroupId,
    "Split Hosting POC",
    "Resources used to validate remote Control Plane projection.");
controlPlane.DefineResources(resources =>
{
    resources
        .AddNetwork("split-sample")
        .WithDisplayName("Split Sample Network")
        .WithResourceGroup(resourceGroupId)
        .WithNetworkKind("Logical")
        .WithHostReadiness("logicalOnly");
});
builder.Services
    .AddNetworkResourceType();
controlPlane.UseResourceGraphIntegration();

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
