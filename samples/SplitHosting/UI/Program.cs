using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Client;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRemoteControlPlane(options =>
{
    builder.Configuration
        .GetSection(RemoteControlPlaneOptions.SectionName)
        .Bind(options);
});

builder
    .AddCloudShellUi()
    .AddExtension(new ResourceManagerExtension(includeSettings: false))
    .AddExtension<ObservabilityExtension>();

var app = builder.Build();

await app.UseCloudShellUiAsync();
app.MapCloudShellUi<App>();

app.Run();
