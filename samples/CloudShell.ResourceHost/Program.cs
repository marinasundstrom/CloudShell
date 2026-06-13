using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceHost;

var builder = WebApplication.CreateBuilder(SampleHostSettings.CreateWebApplicationOptions(args));

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddExtension<SampleResourceExtension>();

cloudShell.Resources(resources =>
{
    var database = resources.Declare(
        SampleResourceProvider.ProviderId,
        "sample:database");

    resources
        .Declare(
            SampleResourceProvider.ProviderId,
            "sample:api")
        .DependsOn(database);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
