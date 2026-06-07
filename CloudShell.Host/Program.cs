using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
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

var cloudShell = builder
    .AddCloudShell()
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

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
