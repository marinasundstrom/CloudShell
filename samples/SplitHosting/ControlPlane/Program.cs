using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

const string resourceGroupId = "split-hosting";

var controlPlane = builder.AddCloudShellControlPlane(controlPlane =>
{
    controlPlane.DefineResources(resources =>
    {
        var resourceGroup = resources.AddResourceGroup(
            resourceGroupId,
            "Split Hosting",
            "Resources used to validate remote Control Plane projection.");

        resources
            .AddNetwork("split-sample")
            .WithDisplayName("Split Sample Network")
            .WithResourceGroup(resourceGroup)
            .WithNetworkKind("Logical")
            .WithHostReadiness("logicalOnly");
    });
});
controlPlane
    .UseNetworkResourceProvider();

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
