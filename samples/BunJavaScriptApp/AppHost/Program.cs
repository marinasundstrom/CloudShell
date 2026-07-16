using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("bun-javascript-app", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "BunJavaScriptApp");

var appPath = app.ResolvePath("..", "App");
var appEndpoint = new Uri(app.Configuration["BunJavaScriptApp:Endpoint"]
    ?? "http://localhost:5174");
var settingsServiceEndpoint = app.Configuration["BunJavaScriptApp:SettingsEndpoint"]
    ?? "http://localhost:5110";
var settingsResourceId = "configuration.store:bun-javascript-app-settings";
var settingsApiEndpoint =
    $"{settingsServiceEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/settings";

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("bun-javascript-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsServiceEndpoint)
        .WithSeed(seed => seed.Setting("Sample--Message", "Hello from the Bun JavaScript app host"));

    resources
        .AddJavaScriptApp("bun-javascript-frontend", appPath)
        .WithDisplayName("Bun JavaScript Frontend")
        .WithBun()
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
            "bun-javascript-frontend")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

return await app.LaunchAsync();
