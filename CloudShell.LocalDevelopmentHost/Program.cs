using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using Microsoft.Extensions.DependencyInjection.Extensions;

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
AddLocalDevelopmentIdentityProvider(builder, cloudShell);

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
builder.Services.AddLocalRabbitMQDockerRuntime();

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
app.MapCloudShellRabbitMQCredentialApi();
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

static void AddLocalDevelopmentIdentityProvider(
    WebApplicationBuilder builder,
    IControlPlaneBuilder controlPlane)
{
    var identity = new InMemoryIdentitySetupOptions
    {
        IsConfigured = true
    };
    builder.Configuration
        .GetSection(InMemoryIdentitySetupOptions.SectionName)
        .Bind(identity);

    builder.Services.Replace(ServiceDescriptor.Singleton(identity));
    controlPlane.AddIdentityProvider(
        identity.ProviderId,
        identity.ProviderName,
        ResourceIdentityProviderKind.BuiltIn,
        useAsDefault: identity.UseAsDefaultProvider);
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
