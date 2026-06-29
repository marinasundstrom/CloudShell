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

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

builder.Services
    .AddStorageBackedSqlServerResourceTypes()
    .AddSqlDatabaseResourceType()
    .AddConfigurationStoreResourceType(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.Entries.Add(new("SampleMessage", "Hello from CloudShell configuration"));
        options.Entries.Add(new("SampleMode", "Development"));
    })
    .AddSecretsVaultResourceType(options =>
    {
        options.ServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
    })
    .AddHostConfigurationSourceResourceType()
    .AddDnsZoneResourceType()
    .AddNameMappingResourceType()
    .AddExecutableApplicationResourceType()
    .AddAspNetCoreProjectResourceType()
    .AddLocalContainerApplicationResourceTypes()
    .AddDockerContainerResourceType();

cloudShell.DefineResources(resources =>
{
    resources
        .AddConfigurationStore("example")
        .WithDisplayName("Example Configuration");
});
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddExtension<DevelopmentShellExtension>();
cloudShell.AddBuiltInProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
