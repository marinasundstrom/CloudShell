using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

var sampleRootPath = Path.GetFullPath("..", builder.Environment.ContentRootPath);
var repositoryRootPath = Path.GetFullPath("../..", sampleRootPath);
var configurationStoreServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.ConfigurationStoreService",
    "CloudShell.ConfigurationStoreService.csproj");
var secretsVaultServiceProjectPath = Path.Combine(
    repositoryRootPath,
    "CloudShell.SecretsVaultService",
    "CloudShell.SecretsVaultService.csproj");
var appPath = Path.Combine(sampleRootPath, "App");
var appEndpoint = builder.Configuration["GoApp:Endpoint"]
    ?? "http://localhost:5186";
var settingsServiceEndpoint = builder.Configuration["GoApp:SettingsEndpoint"]
    ?? "http://localhost:5105";
var secretsServiceEndpoint = builder.Configuration["GoApp:SecretsEndpoint"]
    ?? "http://localhost:6105";
var appEndpointUri = new Uri(appEndpoint);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var group = resources.AddResourceGroup(
                "group:go-app",
                "Go App",
                "Resources for the Go app sample.");

            var settings = resources
                .AddConfigurationStore("go-app-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(group)
                .WithEndpoint(settingsServiceEndpoint)
                .WithAutoStart(false);

            var secrets = resources
                .AddSecretsVault("go-app-secrets")
                .WithDisplayName("Secrets")
                .WithResourceGroup(group)
                .WithEndpoint(secretsServiceEndpoint)
                .WithAutoStart(false);

            resources
                .AddGoApp("go-api", appPath)
                .WithDisplayName("Go API")
                .WithResourceGroup(group)
                .WithAutoStart(false)
                .WithServiceDiscovery()
                .WithReference(settings)
                .WithReference(secrets)
                .WithHttpEndpoint(
                    host: appEndpointUri.Host,
                    port: appEndpointUri.Port,
                    targetPort: appEndpointUri.Port)
                .WithEnvironmentVariable(
                    "PORT",
                    appEndpointUri.Port.ToString())
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "go-api")
                .WithHttpHealthCheck(
                    "/healthz",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http");
        });
    });

cloudShell
    .UseConfigurationStoreResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.Entries.Add(new("Sample--Message", "Hello from the Go app configuration store"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.Secrets.Add(new("Sample--Secret", "local-development-go-secret"));
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
