using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("java-app", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "JavaApp");

var appPath = app.ResolvePath("..", "App");
var artifactPath = Path.Combine("target", "cloudshell-java-app-sample.jar");
var appEndpoint = new Uri(app.Configuration["JavaApp:Endpoint"]
    ?? "http://localhost:5185");
var settingsServiceEndpoint = app.Configuration["JavaApp:SettingsEndpoint"]
    ?? "http://localhost:5104";
var secretsServiceEndpoint = app.Configuration["JavaApp:SecretsEndpoint"]
    ?? "http://localhost:6104";

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("java-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsServiceEndpoint)
        .WithSeed(seed => seed.Setting("Sample--Message", "Hello from the Java app configuration store"));

    var secrets = resources
        .AddSecretsVault("java-app-secrets")
        .WithDisplayName("Secrets")
        .WithEndpoint(secretsServiceEndpoint)
        .WithSeed(seed => seed.Secret("Sample--Secret", "local-development-java-secret"));

    resources
        .AddJavaApp("java-api", appPath, artifactPath)
        .WithDisplayName("Java API")
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
            "java-api")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

return await app.LaunchAsync();
