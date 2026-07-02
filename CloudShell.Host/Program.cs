using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Host.Shell;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;

var builder = WebApplication.CreateBuilder(args);

var configurationStoreServiceProjectPath = Path.GetFullPath(
    "../CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj",
    builder.Environment.ContentRootPath);
var secretsVaultServiceProjectPath = Path.GetFullPath(
    "../CloudShell.SecretsVaultService/CloudShell.SecretsVaultService.csproj",
    builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            resources
                .AddConfigurationStore("example")
                .WithDisplayName("Example Configuration");
        });
    });
builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>()
        .AddExtension<UsageExtension>()
        .AddExtension<DevelopmentShellExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.Entries.Add(new("SampleMessage", "Hello from CloudShell configuration"));
        runtime.Entries.Add(new("SampleMode", "Development"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
    });

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
