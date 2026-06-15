using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

var controlPlane = builder.AddCloudShellControlPlane();

controlPlane.Resources(resources =>
{
    resources
        .AddNetwork("network:split-sample", isDefault: true)
        .WithDisplayName("Split Sample Network")
        .Persist();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
