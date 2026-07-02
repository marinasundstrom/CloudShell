using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;

var builder = CloudShellApplication.CreateBuilder(args);

var sampleRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("../..", sampleRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null);

cloudShell.UseConfigurationStoreResourceProvider(runtime =>
{
    runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
    runtime.ServiceWorkingDirectory = repositoryRootPath;
    runtime.Entries.Add(new("Sample--Message", "Hello from the C# launcher host"));
});

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
