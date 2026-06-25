using System.Text.Json;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;
using ResourceManagerState = CloudShell.Abstractions.ResourceManager.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"]
    ?? cloudShellEndpoint;
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/metrics/ingest";
var frontendEndpoint = builder.Configuration["ProjectReference:FrontendEndpoint"]
    ?? "http://localhost:5218";
var graphApiResourceId = "application.aspnet-core-project:graph-project-reference-api";
var graphApiProjectPath = Path.GetFullPath(
    "../Api/CloudShell.ProjectReferenceApi.csproj",
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services.AddSingleton<
    IResourceModelResourceManagerStateProvider,
    AspNetCoreProjectResourceManagerStateProvider>();
builder.Services.AddSingleton<
    IResourceModelResourceManagerEndpointProjectionProvider,
    AspNetCoreProjectResourceManagerEndpointProjectionProvider>();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-project-reference-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DisplayName: "Graph Project Reference API",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    graphApiProjectPath,
                [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            Host: "localhost",
                            Port: 5229,
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
    .AddAspNetCoreProjectResourceType()
    .AddResourceModelGraphServices()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider(options =>
    {
        options.OtlpEndpoint = otlpEndpoint;
        options.OtlpProtocol = otlpProtocol;
    });

cloudShell.Resources(resources =>
{
    var api = resources.AddAspNetCoreProject(
        "project-reference-api",
        "../Api/CloudShell.ProjectReferenceApi.csproj")
        .WithDisplayName("Project Reference API")
        .WithHttpHealthCheck("/health")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
        .WithEnvironment("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint ?? string.Empty);

    resources
        .AddAspNetCoreProject(
        "project-reference-frontend",
        "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
        endpoint: frontendEndpoint)
        .WithDisplayName("Project Reference Frontend")
        .WithHttpHealthCheck("/healthz")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
        .WithEnvironment("CLOUDSHELL_METRIC_INGEST_ENDPOINT", metricIngestEndpoint ?? string.Empty)
        .WithServiceDiscovery()
        .WithReference(api)
        .DependsOn(api);

    resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphApiResourceId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();

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

    return "http://localhost:5104";
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

internal sealed class AspNetCoreProjectResourceManagerStateProvider(
    IAspNetCoreProjectRuntimeController runtimeController) :
    IResourceModelResourceManagerStateProvider
{
    public ResourceManagerState? GetState(CloudShell.ResourceDefinitions.Resource resource)
    {
        if (resource.Type.TypeId != AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        return runtimeController.GetStatus(resource) switch
        {
            AspNetCoreProjectRuntimeStatus.Running => ResourceManagerState.Running,
            AspNetCoreProjectRuntimeStatus.Stopped => ResourceManagerState.Stopped,
            _ => null
        };
    }
}

internal sealed class AspNetCoreProjectResourceManagerEndpointProjectionProvider :
    IResourceModelResourceManagerEndpointProjectionProvider
{
    public ResourceModelResourceManagerEndpointProjection? GetEndpointProjection(
        CloudShell.ResourceDefinitions.Resource resource)
    {
        if (resource.Type.TypeId != AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        {
            return null;
        }

        var requests = resource.Attributes
            .GetObject<NetworkingEndpointRequestValue[]>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? [];

        if (requests.Length == 0)
        {
            return ResourceModelResourceManagerEndpointProjection.Empty;
        }

        var endpoints = requests
            .Where(request =>
                !string.IsNullOrWhiteSpace(request.Name) &&
                !string.IsNullOrWhiteSpace(request.Protocol))
            .Select(request => new ResourceEndpoint(
                request.Name.Trim(),
                NormalizeProtocol(request.Protocol),
                ParseExposure(request.Exposure),
                request.TargetPort ?? request.Port))
            .ToArray();
        var endpointNetworkMappings = requests
            .Select(request => CreateEndpointNetworkMapping(resource, request))
            .Where(mapping => mapping is not null)
            .Cast<ResourceEndpointNetworkMapping>()
            .ToArray();

        return endpoints.Length == 0 && endpointNetworkMappings.Length == 0
            ? ResourceModelResourceManagerEndpointProjection.Empty
            : new ResourceModelResourceManagerEndpointProjection(
                endpoints,
                EndpointNetworkMappings: endpointNetworkMappings);
    }

    private static ResourceEndpointNetworkMapping? CreateEndpointNetworkMapping(
        CloudShell.ResourceDefinitions.Resource resource,
        NetworkingEndpointRequestValue request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) ||
            string.IsNullOrWhiteSpace(request.Protocol) ||
            request.Port is not > 0)
        {
            return null;
        }

        var host = FirstNonEmpty(request.Host, request.IpAddress);
        if (host is null)
        {
            return null;
        }

        var protocol = NormalizeProtocol(request.Protocol);
        var address = protocol is "http" or "https"
            ? $"{protocol}://{host}:{request.Port.Value}"
            : $"{host}:{request.Port.Value}";

        return ResourceEndpointNetworkMapping.ForEndpoint(
            resource.EffectiveResourceId,
            request.Name,
            address,
            ParseExposure(request.Exposure),
            request.Network?.TryGetResourceId(out var networkResourceId) == true
                ? networkResourceId
                : null);
    }

    private static string NormalizeProtocol(string protocol) =>
        protocol.Trim().ToLowerInvariant();

    private static ResourceExposureScope ParseExposure(string? exposure) =>
        !string.IsNullOrWhiteSpace(exposure) &&
        Enum.TryParse<ResourceExposureScope>(exposure, ignoreCase: true, out var parsed)
            ? parsed
            : ResourceExposureScope.Local;

    private static string? FirstNonEmpty(params string?[] values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return null;
    }
}
