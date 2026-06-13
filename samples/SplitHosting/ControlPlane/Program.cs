using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;

var builder = SampleHostSettings.CreateBuilder(args);

var controlPlane = builder.AddCloudShellControlPlane();

controlPlane.Resources(resources =>
{
    resources
        .AddNetwork("network:split-sample", "Split Sample Network", isDefault: true)
        .Persist();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
