using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using System.Globalization;

var builder = CloudShellApplication.CreateBuilder(args);

const string sampleImageTag = "20260630.1";
const string resourceGroupId = "signalr-container-app";

var hostPort = TryGetConfiguredHostPort(builder.Configuration);
var apiEndpointPort =
    builder.Configuration.GetValue<int?>("SignalRContainerApp:ApiPort") ??
    hostPort + 1 ??
    5095;
var frontendEndpoint = builder.Configuration["SignalRContainerApp:FrontendEndpoint"]
    ?? $"http://localhost:{((hostPort + 5) ?? 5096).ToString(CultureInfo.InvariantCulture)}";
var frontendEndpointUri = new Uri(frontendEndpoint);
var apiIngressEndpoint = $"http://localhost:{apiEndpointPort.ToString(CultureInfo.InvariantCulture)}";
var frontendProjectPath = Path.GetFullPath(
    "Frontend/CloudShell.SignalRContainerApp.Frontend.csproj",
    builder.Environment.ContentRootPath);
var apiProjectPath = Path.GetFullPath(
    "Api/CloudShell.SignalRContainerApp.Api.csproj",
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlane(controlPlane =>
{
    controlPlane.DefineResources(resources =>
    {
        var resourceGroup = resources.AddResourceGroup(
            resourceGroupId,
            "SignalR Container App",
            "Blazor WebAssembly frontend connected to a replicated SignalR container app backend.");

        var containerHostResource = resources
            .GetContainerHost()
            .WithResourceGroup(resourceGroup)
            .WithAutoStart(false);

        var apiResource = resources
            .AddContainerApplication("signalr-api")
            .WithDisplayName("SignalR API")
            .WithResourceGroup(resourceGroup)
            .WithAutoStart(false)
            .UseContainerHost(containerHostResource)
            .WithImage($"cloudshell-signalr-api:{sampleImageTag}")
            .WithProjectPath(apiProjectPath)
            .WithRuntimeLogSources(LogFormat.JsonConsole)
            .WithReplicas(3)
            .WithCookieSessionAffinity("CloudShellSignalRReplica", durationSeconds: 3600)
            .WithHttpEndpoint(
                targetPort: 8080,
                host: "localhost",
                port: apiEndpointPort)
            .WithHttpHealthCheck(
                "/health",
                endpointName: "http")
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                name: "alive");

        resources
            .AddAspNetCoreProject("signalr-frontend", frontendProjectPath)
            .WithDisplayName("SignalR Frontend")
            .WithResourceGroup(resourceGroup)
            .WithAutoStart(false)
            .WithHotReload(false)
            .UseLaunchSettings(false)
            .WithReference(apiResource)
            .DependsOn(apiResource)
            .WithHttpEndpoint(
                host: frontendEndpointUri.Host,
                port: frontendEndpointUri.Port)
            .WithEnvironmentVariable(
                "SignalRBackend__BaseUrl",
                apiIngressEndpoint)
            .WithEnvironmentVariable(
                "ASPNETCORE_ENVIRONMENT",
                "Development")
            .WithHttpHealthCheck(
                "/health",
                endpointName: "http")
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http");
    });
});

builder.Services
    .AddLocalContainerApplicationResourceTypes()
    .AddLocalDockerContainerApplicationRuntime()
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

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

static int? TryGetConfiguredHostPort(IConfiguration configuration)
{
    foreach (var value in new[]
    {
        configuration["urls"],
        configuration["ASPNETCORE_URLS"]
    })
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            continue;
        }

        foreach (var segment in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(segment, UriKind.Absolute, out var uri) && uri.Port > 0)
            {
                return uri.Port;
            }
        }
    }

    return null;
}
