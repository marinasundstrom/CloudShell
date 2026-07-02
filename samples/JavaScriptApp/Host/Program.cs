using CloudShell.Abstractions.Hosting;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
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
var appPath = Path.Combine(sampleRootPath, "App");
var appEndpoint = builder.Configuration["JavaScriptApp:Endpoint"]
    ?? "http://localhost:5173";
var settingsServiceEndpoint = builder.Configuration["JavaScriptApp:SettingsEndpoint"]
    ?? "http://localhost:5101";
var settingsResourceId = "configuration.store:javascript-app-settings";
var settingsEntriesEndpoint =
    $"{settingsServiceEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/entries";
var appEndpointUri = new Uri(appEndpoint);

var cloudShell = builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var group = resources.AddResourceGroup(
                "group:javascript-app",
                "JavaScript App",
                "Resources for the JavaScript app sample.");

            var settings = resources
                .AddConfigurationStore("javascript-app-settings")
                .WithDisplayName("Settings")
                .WithResourceGroup(group)
                .WithEndpoint(settingsServiceEndpoint)
                .WithAutoStart(false);

            resources
                .AddJavaScriptApp("javascript-frontend", appPath)
                .WithDisplayName("JavaScript Frontend")
                .WithResourceGroup(group)
                .WithAutoStart(false)
                .WithPackageManager("npm")
                .WithScript("dev")
                .WithServiceDiscovery()
                .WithReference(settings)
                .WithHttpEndpoint(
                    host: appEndpointUri.Host,
                    port: appEndpointUri.Port,
                    targetPort: appEndpointUri.Port)
                .WithEnvironmentVariable(
                    "PORT",
                    appEndpointUri.Port.ToString())
                .WithEnvironmentVariable(
                    "CLOUDSHELL_SETTINGS_ENDPOINT",
                    settingsEntriesEndpoint)
                .WithEnvironmentVariable(
                    "OTEL_SERVICE_NAME",
                    "javascript-frontend")
                .WithHttpHealthCheck(
                    "/healthz",
                    endpointName: "http")
                .WithHttpLivenessCheck(
                    "/alive",
                    endpointName: "http");
        });
    });

cloudShell.UseConfigurationStoreResourceProvider(runtime =>
{
    runtime.ServiceProjectPath = configurationStoreServiceProjectPath;
    runtime.ServiceWorkingDirectory = repositoryRootPath;
    runtime.Entries.Add(new("Sample--Message", "Hello from the JavaScript app host"));
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
