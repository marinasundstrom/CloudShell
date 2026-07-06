using CloudShell.Abstractions.Logs;
using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;
using System.Globalization;

var app = CloudShellDistributedApplication
    .CreateBuilder("robotic-mower-iot", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "RoboticMowerIoT");

const string sampleImageTag = "20260706.1";

var backendPort = ReadPort(app.Configuration["RoboticMowerIoT:BackendPort"], 7161);
var frontendEndpoint = new Uri(app.Configuration["RoboticMowerIoT:FrontendEndpoint"] ??
    "http://localhost:7162");
var registryEndpoint = app.Configuration["RoboticMowerIoT:DeviceRegistryEndpoint"] ??
    "http://localhost:7160";
var backendEndpoint = $"http://localhost:{backendPort.ToString(CultureInfo.InvariantCulture)}";
var backendProjectPath = app.ResolvePath("..", "Backend", "CloudShell.RoboticMowerIoT.Backend.csproj");
var frontendProjectPath = app.ResolvePath("..", "Frontend");

app.DefineResources(resources =>
{
    var containerHostResource = resources
        .GetContainerHost();

    var registry = resources
        .AddDeviceRegistry("park-devices")
        .WithDisplayName("Park Device Registry")
        .WithEndpoint(registryEndpoint)
        .WithHeartbeatStaleAfter(TimeSpan.FromMinutes(5))
        .UseEnrollmentProfile(profile =>
        {
            profile
                .WithName("robotic-mowers")
                .AllowSubjectPrefix("mower/")
                .RequireClaim("manufacturer", "cloudshell")
                .RequireClaim("deviceType", "robotic-mower");
        });

    var backend = resources
        .AddContainerApplication("mower-backend")
        .WithDisplayName("Mower Coordination Backend")
        .UseContainerHost(containerHostResource)
        .WithImage($"cloudshell-mower-backend:{sampleImageTag}")
        .WithProjectPath(backendProjectPath)
        .WithRuntimeLogSources(LogFormat.JsonConsole)
        .WithReplicas(1)
        .WithHttpEndpoint(
            targetPort: 8080,
            host: "localhost",
            port: backendPort)
        .WithHttpHealthCheck(
            "/health",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http",
            name: "alive")
        .DependsOn(registry);

    resources
        .AddJavaScriptApp("mower-frontend", frontendProjectPath)
        .WithDisplayName("Mower Operations Frontend")
        .WithPackageManager("npm")
        .WithScript("dev")
        .WithServiceDiscovery()
        .WithReference(backend)
        .DependsOn(backend)
        .WithHttpEndpoint(
            host: frontendEndpoint.Host,
            port: frontendEndpoint.Port,
            targetPort: frontendEndpoint.Port)
        .WithEnvironmentVariable(
            "PORT",
            frontendEndpoint.Port.ToString(CultureInfo.InvariantCulture))
        .WithEnvironmentVariable(
            "VITE_BACKEND_URL",
            backendEndpoint)
        .WithEnvironmentVariable(
            "VITE_BACKEND_HUB_URL",
            $"{backendEndpoint}/hubs/mowers")
        .WithEnvironmentVariable(
            "VITE_PARK_NAME",
            "North Meadow Park")
        .WithHttpHealthCheck(
            "/",
            endpointName: "http");
});

return await app.LaunchAsync();

static int ReadPort(string? value, int fallback) =>
    int.TryParse(value, out var port) && port > 0
        ? port
        : fallback;
