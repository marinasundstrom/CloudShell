using CloudShell.Abstractions.Hosting;
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

var apiEndpointPort = builder.Configuration.GetValue<int?>("SignalRContainerApp:ApiPort") ?? 5095;
var frontendEndpoint = builder.Configuration["SignalRContainerApp:FrontendEndpoint"]
    ?? "http://localhost:5096";
var frontendEndpointUri = new Uri(frontendEndpoint);
var apiIngressEndpoint = $"http://localhost:{apiEndpointPort.ToString(CultureInfo.InvariantCulture)}";
var frontendProjectPath = Path.GetFullPath(
    "Frontend/CloudShell.SignalRContainerApp.Frontend.csproj",
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "SignalR Container App",
    "Blazor WebAssembly frontend connected to a replicated SignalR container app backend.");
IResourceDefinitionBuilder apiResource = null!;
cloudShell.DefineResources(resources =>
{
    apiResource = resources
        .AddContainerApplication("signalr-api")
        .WithDisplayName("SignalR API")
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false)
        .WithImage($"cloudshell-signalr-api:{sampleImageTag}")
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
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false)
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithReference(apiResource)
        .WithHttpEndpoint(
            host: frontendEndpointUri.Host,
            port: frontendEndpointUri.Port)
        .WithEnvironmentVariable(
            "SignalRBackend__BaseUrl",
            apiIngressEndpoint)
        .WithHttpHealthCheck(
            "/health",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});

builder.Services
    .AddLocalContainerApplicationResourceTypes()
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

cloudShell.AddBuiltInProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
