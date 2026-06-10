using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Configuration;

var builder = WebApplication.CreateBuilder(args);
var repositoryRootPath = Path.GetFullPath("../..", builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddConfigurationProvider(options =>
    {
        options.ServiceProjectPath = configurationStoreServiceProjectPath;
        options.SecretsServiceProjectPath = secretsVaultServiceProjectPath;
        options.ServiceWorkingDirectory = repositoryRootPath;
        options.SecretsServiceWorkingDirectory = repositoryRootPath;
    });

cloudShell.Resources(resources =>
{
    var settings = resources
        .AddConfigurationStore(
            "configuration:sample-app",
            "Sample App Settings")
        .WithEntries(
        [
            new("Sample:Message", "Hello from a configuration entry"),
            new("Sample:Mode", "Development")
        ]);

    var secrets = resources
        .AddSecretsVault("secrets-vault:sample-app", "Sample App Secrets")
        .WithSecret("sample-api-key", "local-development-api-key");

    resources
        .AddAspNetCoreProject(
            "application:settings-secrets-api",
            "Settings and Secrets API",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            endpoint: "http://localhost:5227")
        .WithEnvironment("SAMPLE_MESSAGE", settings.Entry("Sample:Message"))
        .WithEnvironment("SAMPLE_MODE", settings.Entry("Sample:Mode"))
        .WithEnvironment("SAMPLE_API_KEY", secrets.Secret("sample-api-key"))
        .WithAutoStart(false);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
