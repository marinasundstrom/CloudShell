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
var appPath = Path.Combine(sampleRootPath, "App");
var appEndpoint = builder.Configuration["JavaScriptApp:Endpoint"]
    ?? "http://localhost:5173";
var settingsEndpoint = builder.Configuration["JavaScriptApp:SettingsEndpoint"]
    ?? "http://localhost:5101/api/configuration/stores/javascript-app-settings/entries";
var appEndpointUri = new Uri(appEndpoint);

builder.AddCloudShellControlPlaneApplication(
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
                .WithEndpoint(settingsEndpoint)
                .WithAutoStart(false);

            resources
                .AddJavaScriptApp("javascript-frontend", appPath)
                .WithDisplayName("JavaScript Frontend")
                .WithResourceGroup(group)
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
                    settingsEndpoint)
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
builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
