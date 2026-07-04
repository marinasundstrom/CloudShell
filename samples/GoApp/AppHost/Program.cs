using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("go-app", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "GoApp");

var appPath = app.ResolvePath("..", "App");
var appEndpoint = new Uri(app.Configuration["GoApp:Endpoint"]
    ?? "http://localhost:5186");
var settingsServiceEndpoint = app.Configuration["GoApp:SettingsEndpoint"]
    ?? "http://localhost:5105";
var secretsServiceEndpoint = app.Configuration["GoApp:SecretsEndpoint"]
    ?? "http://localhost:6105";

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("go-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsServiceEndpoint)
        .WithSeed(seed => seed.Setting("Sample--Message", "Hello from the Go app configuration store"));

    var secrets = resources
        .AddSecretsVault("go-app-secrets")
        .WithDisplayName("Secrets")
        .WithEndpoint(secretsServiceEndpoint)
        .WithSeed(seed => seed.Secret("Sample--Secret", "local-development-go-secret"));

    resources
        .AddGoApp("go-api", appPath)
        .WithDisplayName("Go API")
        .WithServiceDiscovery()
        .WithReference(settings)
        .WithReference(secrets)
        .WithHttpEndpoint(
            host: appEndpoint.Host,
            port: appEndpoint.Port,
            targetPort: appEndpoint.Port)
        .WithEnvironmentVariable(
            "PORT",
            appEndpoint.Port.ToString())
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

return await app.LaunchAsync();
