using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("javascript-app", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "JavaScriptApp");

var appPath = app.ResolvePath("..", "App");
var appEndpoint = new Uri(app.Configuration["JavaScriptApp:Endpoint"]
    ?? "http://localhost:5173");
var settingsServiceEndpoint = app.Configuration["JavaScriptApp:SettingsEndpoint"]
    ?? "http://localhost:5101";
var settingsResourceId = "configuration.store:javascript-app-settings";
var settingsApiEndpoint =
    $"{settingsServiceEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/entries";

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("javascript-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsServiceEndpoint)
        .WithSeed(seed => seed.Setting("Sample--Message", "Hello from the JavaScript app host"));

    resources
        .AddJavaScriptApp("javascript-frontend", appPath)
        .WithDisplayName("JavaScript Frontend")
        .WithPackageManager("npm")
        .WithScript("dev")
        .WithServiceDiscovery()
        .WithReference(settings)
        .WithHttpEndpoint(
            host: appEndpoint.Host,
            port: appEndpoint.Port,
            targetPort: appEndpoint.Port)
        .WithEnvironmentVariable(
            "PORT",
            appEndpoint.Port.ToString())
        .WithEnvironmentVariable(
            "CLOUDSHELL_SETTINGS_ENDPOINT",
            settingsApiEndpoint)
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

return await app.LaunchAsync();
