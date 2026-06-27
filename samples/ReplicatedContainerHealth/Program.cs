using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using System.Text.Json;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

const string sampleImageTag = "20260622.2";
const string graphResourceGroupId = "replicated-container-health-graph-poc";
const string graphDockerResourceId = "docker:graph-sample";
const string graphApiResourceId = "application.container-app:graph-api";

var apiEndpointPort = builder.Configuration.GetValue<int?>("ReplicatedContainerHealth:ApiPort") ?? 5092;
var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var runtimeControlPlaneEndpoint = builder.Configuration["Observability:RuntimeEndpoint"]
    ?? ResolveDockerReachableEndpoint(cloudShellEndpoint);
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"]
    ?? runtimeControlPlaneEndpoint;
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{runtimeControlPlaneEndpoint}/api/control-plane/v1/metrics/ingest";
var graphOnly = builder.Configuration.GetValue("ReplicatedContainerHealth:GraphOnly", false);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddSingleton<IReplicatedContainerHealthGraphContainerAppRuntimeBridge>(
            serviceProvider => graphOnly
                ? new ReplicatedContainerHealthGraphOnlyContainerAppRuntimeBridge()
                : new ReplicatedContainerHealthGraphResourceManagerBridge(
                    serviceProvider.GetRequiredService<IServiceScopeFactory>()))
    .AddSingleton<IContainerApplicationRuntimeHandler, ReplicatedContainerHealthGraphRuntimeHandler>()
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-sample",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ResourceId: graphDockerResourceId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId),
        new ResourceGraphState(
            "graph-api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: graphApiResourceId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DisplayName: "Graph Replicated API",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    $"cloudshell-application-api:{sampleImageTag}",
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas] =
                    3,
                [ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            TargetPort: 8080,
                            Host: "localhost",
                            Port: apiEndpointPort,
                            Exposure: "Local")
                    })
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [ResourceHealthCheckCapabilityIds.HealthChecks] =
                    ResourceDefinitionJson.FromValue(new ResourceHealthCheckDefinitionSet(
                    [
                        ResourceHealthCheckDefinition.Http(
                            "/health",
                            endpointName: "http"),
                        ResourceHealthCheckDefinition.HttpLiveness(
                            "/alive",
                            endpointName: "http",
                            name: "alive")
                    ]))
            })
    ])
    .AddDockerHostResourceType()
    .AddContainerApplicationResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell
        .AddApplicationProvider(options =>
        {
            options.OtlpEndpoint = otlpEndpoint;
            options.OtlpProtocol = otlpProtocol;
        })
        .AddDockerProvider();
}

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Replicated Container Health graph POC",
        "Side-by-side graph-backed resources used while porting the ReplicatedContainerHealth sample.");

    if (!graphOnly)
    {
        var docker = resources
            .AddDocker("sample")
            .Persist(overwrite: true);

        resources
            .AddAspNetCoreProject(
                "api",
                "Api/CloudShell.ReplicatedContainerHealth.Api.csproj")
            .AsContainer(replicas: 3, tag: sampleImageTag)
            .WithEndpointPort(
                "http",
                targetPort: 8080,
                port: apiEndpointPort,
                protocol: "http",
                exposure: ResourceExposureScope.Local)
            .WithHttpHealthCheck("/health", "http")
            .WithHttpProbe(ResourceProbeType.Liveness, "/alive", "http", "alive")
            .WithLogFormat(LogFormat.JsonConsole)
            .WithOtlpExporter(otlpEndpoint, otlpProtocol)
            .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
            .WithEnvironment("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint ?? string.Empty)
            .WithContainerHost(docker)
            .WithAutoStart(false)
            .Persist(overwrite: true);
    }

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDockerResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId)
        .WithResourceGroup(graphResourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();
app.MapPost(
    "/replicated-container-health/resource-graph/resources/{resourceId}/container-image",
    async (
        string resourceId,
        ReplicatedContainerHealthGraphImageUpdate update,
        ResourceModelGraphDefinitionApplyService applyService,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(resourceId, graphApiResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(update.Image))
        {
            return Results.BadRequest("Container image is required.");
        }

        var result = await applyService.ApplyDefinitionsAsync(
            [CreateGraphApiImageDefinition(update.Image.Trim())],
            new ResourceGraphCommitContext(
                EnvironmentId: "replicated-container-health",
                PrincipalId: "sample",
                Timestamp: DateTimeOffset.UtcNow),
            cancellationToken);

        return Results.Ok(ReplicatedContainerHealthGraphApplyResponse.FromResult(result));
    });

app.Run();

static ResourceDefinition CreateGraphApiImageDefinition(string image) =>
    new(
        "graph-api",
        ContainerApplicationResourceTypeProvider.ResourceTypeId,
        ResourceId: "application.container-app:graph-api",
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
        uri.Host is not ("localhost" or "127.0.0.1" or "::1"))
    {
        return endpoint;
    }

    var builder = new UriBuilder(uri)
    {
        Host = "host.docker.internal"
    };
    return builder.Uri.GetLeftPart(UriPartial.Authority);
}

internal sealed record ReplicatedContainerHealthGraphImageUpdate(
    string Image);

internal sealed record ReplicatedContainerHealthGraphApplyResponse(
    bool Committed,
    bool HasErrors,
    long BaseVersion,
    long ResultVersion,
    string Status,
    IReadOnlyList<ReplicatedContainerHealthGraphApplyDiagnosticResponse> Diagnostics)
{
    public static ReplicatedContainerHealthGraphApplyResponse FromResult(
        ResourceModelGraphDefinitionApplyResult result) =>
        new(
            result.IsCommitted,
            result.HasErrors,
            result.BaseVersion.Value,
            result.Commit.Version.Value,
            result.Commit.Summary.Status.ToString(),
            result.Diagnostics
                .Select(diagnostic => new ReplicatedContainerHealthGraphApplyDiagnosticResponse(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Target))
                .ToArray());
}

internal sealed record ReplicatedContainerHealthGraphApplyDiagnosticResponse(
    string Severity,
    string Code,
    string Message,
    string? Target);
