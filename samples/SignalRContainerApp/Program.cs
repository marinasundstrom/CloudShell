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
const string apiResourceId = "application.container-app:signalr-api";

var hostPort = TryGetConfiguredHostPort(builder.Configuration);
var apiEndpointPort =
    builder.Configuration.GetValue<int?>("SignalRContainerApp:ApiPort") ??
    hostPort + 1 ??
    5095;
var frontendEndpoint = builder.Configuration["SignalRContainerApp:FrontendEndpoint"]
    ?? $"http://localhost:{((hostPort + 5) ?? 5096).ToString(CultureInfo.InvariantCulture)}";
var frontendEndpointUri = new Uri(frontendEndpoint);
var apiIngressEndpoint = $"http://localhost:{apiEndpointPort.ToString(CultureInfo.InvariantCulture)}";
var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var frontendProjectPath = Path.GetFullPath(
    "Frontend/CloudShell.SignalRContainerApp.Frontend.csproj",
    builder.Environment.ContentRootPath);
var apiProjectPath = Path.GetFullPath(
    "Api/CloudShell.SignalRContainerApp.Api.csproj",
    builder.Environment.ContentRootPath);
var runtimeControlPlaneEndpoint = builder.Configuration["SignalRContainerApp:RuntimeControlPlaneEndpoint"]
    ?? ResolveDockerReachableEndpoint(cloudShellEndpoint);
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/metrics/ingest";

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
    .AddLocalDockerContainerApplicationRuntime(options =>
        options.AddApplication(
            apiResourceId,
            apiProjectPath,
            runtime =>
            {
                runtime.IngressContainerName = "cloudshell-signalr-api-ingress";
                runtime.IngressConfigurationDirectory = Path.Combine(
                    builder.Environment.ContentRootPath,
                    "Data",
                    "signalr-api-ingress");
                runtime.ReplicaContainerNamePrefix = "cloudshell-signalr-api-replica-";
                runtime.ReplicaNetworkAliasPrefix = "cloudshell-signalr-api-replica-";
                runtime.ReplicaResourceIdPrefix = $"{apiResourceId}:replica-";
                runtime.ReplicaServiceNamePrefix = "signalr-api-replica-";
                runtime.RuntimeResourceProviderId = "signalr-container-app.runtime";
                runtime.RuntimeResourceProviderName = "SignalR container app runtime";
                runtime.RuntimeMaterialization = "signalrDockerRuntime";
                runtime.TraceIngestEndpoint = traceIngestEndpoint;
                runtime.MetricIngestEndpoint = metricIngestEndpoint;
                runtime.ReplicaProbePortStart =
                    builder.Configuration.GetValue<int?>("SignalRContainerApp:RuntimeProbePortStart") ??
                    builder.Configuration.GetValue<int?>("SignalRContainerApp:ReplicaPortStart");
                runtime.StatusProbeTimeout = TimeSpan.FromMilliseconds(
                    builder.Configuration.GetValue<int?>(
                        "SignalRContainerApp:RuntimeStatusProbeTimeoutMilliseconds") ?? 1_000);
                runtime.StatusCacheDuration = TimeSpan.FromMilliseconds(
                    builder.Configuration.GetValue<int?>(
                        "SignalRContainerApp:RuntimeStatusCacheMilliseconds") ?? 2_000);
                runtime.ReplicaCleanupLimit = builder.Configuration.GetValue<int?>(
                    "SignalRContainerApp:RuntimeReplicaCleanupLimit");
            }))
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

static string ResolveCloudShellEndpoint(IConfiguration configuration)
{
    var configuredEndpoint = FirstHttpEndpoint(configuration["Observability:Endpoint"]);
    if (configuredEndpoint is not null)
    {
        return configuredEndpoint;
    }

    var urlsEndpoint = FirstHttpEndpoint(configuration["urls"]);
    if (urlsEndpoint is not null)
    {
        return urlsEndpoint;
    }

    var aspNetCoreUrlsEndpoint = FirstHttpEndpoint(
        Environment.GetEnvironmentVariable("ASPNETCORE_URLS"));
    if (aspNetCoreUrlsEndpoint is not null)
    {
        return aspNetCoreUrlsEndpoint;
    }

    return $"http://localhost:{configuration.GetValue<int?>("PORT") ?? 5094}";
}

static string? FirstHttpEndpoint(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    foreach (var candidate in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (Uri.TryCreate(candidate, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https")
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }
    }

    return null;
}

static string ResolveDockerReachableEndpoint(string endpoint)
{
    if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) ||
        uri.Host is not ("localhost" or "127.0.0.1" or "::1" or "0.0.0.0" or "::"))
    {
        return endpoint;
    }

    var builder = new UriBuilder(uri)
    {
        Host = "host.docker.internal"
    };
    return builder.Uri.GetLeftPart(UriPartial.Authority);
}
