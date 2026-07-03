using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;

var app = CloudShellDistributedApplication
    .CreateBuilder("csharp-app-host", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "CSharpAppHost");

var javascriptAppPath = app.ResolvePath("..", "..", "JavaScriptApp", "App");
var settingsEndpoint = app.Configuration["CSharpAppHost:SettingsEndpoint"]
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_SETTINGS_ENDPOINT")
    ?? "http://localhost:5103";
var settingsResourceId = "configuration.store:csharp-app-settings";
var settingsEntriesEndpoint =
    $"{settingsEndpoint.TrimEnd('/')}/api/configuration/stores/{Uri.EscapeDataString(settingsResourceId)}/entries";
var appEndpoint = new Uri(app.Configuration["CSharpAppHost:AppEndpoint"]
    ?? Environment.GetEnvironmentVariable("CLOUDSHELL_APP_ENDPOINT")
    ?? "http://localhost:5175");

app.DefineResources(resources =>
{
    var settings = resources
        .AddConfigurationStore("csharp-app-settings")
        .WithDisplayName("Settings")
        .WithEndpoint(settingsEndpoint)
        .WithSetting("Sample--Message", "Hello from C# launcher seed")
        .WithAutoStart(false);

    var secrets = resources
        .AddSecretsVault("csharp-app-secrets")
        .WithDisplayName("Secrets")
        .WithEndpoint("http://localhost:6103")
        .WithSecret("Sample--ApiKey", "csharp-launcher-secret", "v1")
        .WithAutoStart(false);

    resources
        .AddJavaScriptApp("csharp-declared-frontend", javascriptAppPath)
        .WithDisplayName("C# Declared Frontend")
        .WithAutoStart(false)
        .WithPackageManager("npm")
        .WithScript("dev")
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
            "CLOUDSHELL_SETTINGS_ENDPOINT",
            settingsEntriesEndpoint)
        .WithEnvironmentVariable(
            "Sample__Message",
            settings.Entry("Sample--Message"))
        .WithEnvironmentVariable(
            "Sample__ApiKey",
            secrets.Secret("Sample--ApiKey"))
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "csharp-declared-frontend")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

return await app.LaunchAsync();
