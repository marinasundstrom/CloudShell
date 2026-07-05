using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("python-app-host", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "PythonAppHost");

var pythonAppPath = app.ResolvePath("..", "App");
var appEndpoint = new Uri(app.Configuration["PythonAppHost:AppEndpoint"]
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_APP_ENDPOINT")
    ?? "http://localhost:5188");
var settingsEndpoint = app.Configuration["PythonAppHost:SettingsEndpoint"]
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT")
    ?? "http://localhost:5108";
var secretsEndpoint = app.Configuration["PythonAppHost:SecretsEndpoint"]
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_SECRETS_ENDPOINT")
    ?? "http://localhost:6108";

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("python-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsEndpoint)
        .WithSeed(seed => seed.Setting("Sample--Message", "Hello from the Python app configuration store"));

    var secrets = resources
        .AddSecretsVault("python-app-secrets")
        .WithDisplayName("Secrets")
        .WithEndpoint(secretsEndpoint)
        .WithSeed(seed => seed.Secret("Sample--ApiKey", "python-local-development-secret", "v1"));

    resources
        .AddPythonApp("python-api", pythonAppPath)
        .WithDisplayName("Python API")
        .WithServiceDiscovery()
        .WithReference(settings)
        .WithReference(secrets)
        .DependsOn(settings)
        .DependsOn(secrets)
        .WithHttpEndpoint(
            host: appEndpoint.Host,
            port: appEndpoint.Port,
            targetPort: appEndpoint.Port)
        .WithEnvironmentVariable(
            "PORT",
            appEndpoint.Port.ToString())
        .WithEnvironmentVariable(
            "Sample__Message",
            settings.Setting("Sample--Message"))
        .WithEnvironmentVariable(
            "Sample__ApiKey",
            secrets.Secret("Sample--ApiKey"))
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "python-api")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

return await app.LaunchAsync();
