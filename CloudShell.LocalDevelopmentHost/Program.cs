using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;

var builder = WebApplication.CreateBuilder(args);
AddDelegatedHostSettings(builder.Configuration, args);

var repositoryRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");
var cloudShellDataDirectory = ResolveCloudShellDataDirectory(
    builder.Configuration,
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null);

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "configuration-store-definitions");
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.DefinitionsDirectory = Path.Combine(
            cloudShellDataDirectory,
            "secrets-vault-definitions");
    });

builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();

static string ResolveCloudShellDataDirectory(
    IConfiguration configuration,
    string contentRootPath)
{
    var configuredPath = configuration["CloudShell:DataDirectory"];
    var settingsBasePath = ResolveHostSettingsBasePath(configuration, contentRootPath);
    var path = string.IsNullOrWhiteSpace(configuredPath)
        ? contentRootPath
        : Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(configuredPath, settingsBasePath);
    Directory.CreateDirectory(path);
    return path;
}

static string ResolveHostSettingsBasePath(
    IConfiguration configuration,
    string contentRootPath)
{
    var hostSettingsPath = configuration["CloudShell:HostSettingsPath"];
    return string.IsNullOrWhiteSpace(hostSettingsPath)
        ? contentRootPath
        : Path.GetDirectoryName(Path.GetFullPath(hostSettingsPath)) ?? contentRootPath;
}

static void AddDelegatedHostSettings(
    ConfigurationManager configuration,
    string[] args)
{
    var hostSettingsPath = configuration["CloudShell:HostSettingsPath"];
    if (string.IsNullOrWhiteSpace(hostSettingsPath))
    {
        return;
    }

    configuration
        .AddJsonFile(Path.GetFullPath(hostSettingsPath), optional: false)
        .AddEnvironmentVariables()
        .AddCommandLine(args);
}
