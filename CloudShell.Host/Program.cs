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
var configurationServiceProjectPath = Path.GetFullPath(
    "../CloudShell.ConfigurationService/CloudShell.ConfigurationService.csproj",
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
        options.ServiceProjectPath = configurationServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
    })
    .AddApplicationProvider(activationPolicy: CloudShellExtensionActivationPolicy.UserManaged)
    .AddDockerProvider(activationPolicy: CloudShellExtensionActivationPolicy.UserManaged);

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore("configuration:example", "Example Configuration")
        .WithEntries(
        [
            new("SampleMessage", "Hello from CloudShell configuration"),
            new("SampleSecret", "local-development-secret", IsSecret: true)
        ]);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
