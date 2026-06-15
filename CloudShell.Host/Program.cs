using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Host.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;
using CloudShell.Providers.Docker;

var builder = WebApplication.CreateBuilder(args);

var configurationStoreDefinitionsPath = Path.GetFullPath(
    "Data/configuration-stores.json",
    builder.Environment.ContentRootPath);
var secretsVaultDefinitionsPath = Path.GetFullPath(
    "Data/secrets-vaults.json",
    builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.GetFullPath(
    "../CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj",
    builder.Environment.ContentRootPath);
var secretsVaultServiceProjectPath = Path.GetFullPath(
    "../CloudShell.SecretsVaultService/CloudShell.SecretsVaultService.csproj",
    builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddExtension<DevelopmentShellExtension>()
    .AddConfigurationProvider(options =>
    {
        options.DefinitionsPath = configurationStoreDefinitionsPath;
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
    })
    .AddSecretsProvider(options =>
    {
        options.SecretsVaultDefinitionsPath = secretsVaultDefinitionsPath;
        options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
        options.SecretsServiceWorkingDirectory = repositoryRootPath;
    })
    .AddApplicationProvider(activationPolicy: CloudShellExtensionActivationPolicy.UserManaged)
    .AddDockerProvider(activationPolicy: CloudShellExtensionActivationPolicy.UserManaged);

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example")
        .WithEntries(
        [
            new("SampleMessage", "Hello from CloudShell configuration"),
            new("SampleMode", "Development")
        ]);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
