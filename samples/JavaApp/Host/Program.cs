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
var artifactPath = Path.Combine("target", "cloudshell-java-app-sample.jar");
var appEndpoint = builder.Configuration["JavaApp:Endpoint"]
    ?? "http://localhost:5185";
var settingsServiceEndpoint = builder.Configuration["JavaApp:SettingsEndpoint"]
    ?? "http://localhost:5104";
var secretsServiceEndpoint = builder.Configuration["JavaApp:SecretsEndpoint"]
    ?? "http://localhost:6104";
var appEndpointUri = new Uri(appEndpoint);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var group = resources.AddResourceGroup(
                "group:java-app",
                "Java App",
                "Resources for the Java app sample.");

            var settings = resources
                .AddConfigurationStore("java-app-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(group)
                .WithEndpoint(settingsServiceEndpoint)
                .WithAutoStart(false);

            var secrets = resources
                .AddSecretsVault("java-app-secrets")
                .WithDisplayName("Secrets")
                .WithResourceGroup(group)
                .WithEndpoint(secretsServiceEndpoint)
                .WithAutoStart(false);

            resources
                .AddJavaApp("java-api", appPath, artifactPath)
                .WithDisplayName("Java API")
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
                    "java-api")
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
        runtime.Entries.Add(new("Sample--Message", "Hello from the Java app configuration store"));
    })
    .UseSecretsVaultResourceProvider(runtime =>
    {
        runtime.ServiceProjectPath = secretsVaultServiceProjectPath;
        runtime.ServiceWorkingDirectory = repositoryRootPath;
        runtime.Secrets.Add(new("Sample--Secret", "local-development-java-secret"));
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
