using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager.UI;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string sampleImageTag = "20260622.2";
const string resourceGroupId = "replicated-container-health-poc";

var apiEndpointPort = builder.Configuration.GetValue<int?>("ReplicatedContainerHealth:ApiPort") ?? 5092;
var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var runtimeControlPlaneEndpoint = builder.Configuration["Observability:RuntimeEndpoint"]
    ?? ResolveDockerReachableEndpoint(cloudShellEndpoint);
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/metrics/ingest";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Replicated Container Health POC",
    "Resource model resources used by the ReplicatedContainerHealth sample.");
IResourceDefinitionBuilder dockerResource = null!;
IResourceDefinitionBuilder apiResource = null!;
cloudShell.DefineResources(resources =>
{
    dockerResource = resources
        .AddDockerHost("sample")
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);

    apiResource = resources
        .AddContainerApplication("api")
        .WithDisplayName("Replicated API")
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false)
        .UseDockerHost(dockerResource)
        .WithImage($"cloudshell-application-api:{sampleImageTag}")
        .WithReplicas(3)
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
});
builder.Services
    .AddSingleton<IReplicatedContainerHealthCommandRunner, ProcessReplicatedContainerHealthCommandRunner>()
    .AddSingleton<IReplicatedContainerHealthContainerAppRuntimeBridge>(
        serviceProvider => new ReplicatedContainerHealthContainerAppRuntimeBridge(
            serviceProvider.GetRequiredService<IReplicatedContainerHealthCommandRunner>(),
            serviceProvider.GetRequiredService<IConfiguration>(),
            serviceProvider.GetRequiredService<IHostEnvironment>(),
            traceIngestEndpoint,
            metricIngestEndpoint))
    .AddSingleton<IContainerApplicationRuntimeHandler, ReplicatedContainerHealthContainerAppRuntimeHandler>()
    .AddLocalContainerApplicationResourceTypes();
cloudShell.UseResourceGraphIntegration();
builder.Services.AddSingleton<IResourceOrchestrationDescriptorProvider, ReplicatedContainerHealthOrchestrationDescriptorProvider>();
builder.Services.AddScoped<IResourceProvider, ReplicatedContainerHealthRuntimeResourceProvider>();
builder.Services.AddScoped<ILogProvider, ReplicatedContainerHealthRuntimeLogProvider>();
builder.Services.AddScoped<IResourceMonitoringProvider, ReplicatedContainerHealthRuntimeMonitoringProvider>();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

cloudShell.AddReferenceProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();
app.MapPost(
    "/replicated-container-health/resource-graph/resources/{resourceId}/container-image",
    async (
        string resourceId,
        ReplicatedContainerHealthImageUpdate update,
        ResourceModelGraphDefinitionApplyService applyService,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(resourceId, apiResource.EffectiveResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(update.Image))
        {
            return Results.BadRequest("Container image is required.");
        }

        var result = await applyService.ApplyDefinitionsAsync(
            [CreateApiImageDefinition(update.Image.Trim())],
            new ResourceGraphCommitContext(
                EnvironmentId: "replicated-container-health",
                PrincipalId: "sample",
                Timestamp: DateTimeOffset.UtcNow),
            cancellationToken);

        return Results.Ok(ReplicatedContainerHealthApplyResponse.FromResult(result));
    });

app.Run();

static ResourceDefinition CreateApiImageDefinition(string image) =>
    new(
        "api",
        ContainerApplicationResourceTypeProvider.ResourceTypeId,
        ResourceId: "application.container-app:api",
        ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] = image
        });

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

    return "http://localhost:5011";
}

static string? FirstHttpEndpoint(string? value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return null;
    }

    foreach (var candidate in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri))
        {
            continue;
        }

        if (uri.Scheme is "http" or "https")
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

internal sealed record ReplicatedContainerHealthImageUpdate(
    string Image);

internal sealed record ReplicatedContainerHealthApplyResponse(
    bool Committed,
    bool HasErrors,
    long BaseVersion,
    long ResultVersion,
    string Status,
    IReadOnlyList<ReplicatedContainerHealthApplyDiagnosticResponse> Diagnostics)
{
    public static ReplicatedContainerHealthApplyResponse FromResult(
        ResourceModelGraphDefinitionApplyResult result) =>
        new(
            result.IsCommitted,
            result.HasErrors,
            result.BaseVersion.Value,
            result.Commit.Version.Value,
            result.Commit.Summary.Status.ToString(),
            result.Diagnostics
                .Select(diagnostic => new ReplicatedContainerHealthApplyDiagnosticResponse(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Target))
                .ToArray());
}

internal sealed record ReplicatedContainerHealthApplyDiagnosticResponse(
    string Severity,
    string Code,
    string Message,
    string? Target);
