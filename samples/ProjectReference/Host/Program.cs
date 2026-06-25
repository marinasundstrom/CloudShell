using System.Text.Json;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
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
var graphApiEndpoint = builder.Configuration["ProjectReference:GraphApiEndpoint"]
    ?? "http://localhost:5229";
var graphApiEndpointUri = new Uri(graphApiEndpoint);
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
builder.Services.AddSingleton<
    IResourceModelResourceManagerObservabilityProvider,
    AspNetCoreProjectResourceManagerObservabilityProvider>();
builder.Services.AddSingleton<ILogProvider, AspNetCoreProjectResourceManagerLogProvider>();
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
                            graphApiEndpointUri.Scheme,
                            Host: graphApiEndpointUri.Host,
                            Port: graphApiEndpointUri.Port,
                            Exposure: "Local")
                    }),
                [AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                            traceIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
                            metricIngestEndpoint ?? string.Empty),
                        new AspNetCoreProjectEnvironmentVariableValue(
                            "OTEL_SERVICE_NAME",
                            "graph-project-reference-api")
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
app.MapPost(
    "/project-reference/resource-graph/resources/{resourceId}/environment-variables",
    async (
        string resourceId,
        ProjectReferenceGraphEnvironmentVariableUpdate update,
        ResourceModelGraphDefinitionApplyService applyService,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(resourceId, graphApiResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(update.Name))
        {
            return Results.BadRequest("Environment variable name is required.");
        }

        var definition = CreateGraphApiDefinition(
            graphApiEndpointUri,
            graphApiProjectPath,
            traceIngestEndpoint,
            metricIngestEndpoint,
            new AspNetCoreProjectEnvironmentVariableValue(update.Name.Trim(), update.Value));
        var result = await applyService.ApplyDefinitionsAsync(
            [definition],
            new ResourceGraphCommitContext(
                EnvironmentId: "project-reference",
                PrincipalId: "sample",
                Timestamp: DateTimeOffset.UtcNow),
            cancellationToken);

        return Results.Ok(ProjectReferenceGraphApplyResponse.FromResult(result));
    });

app.Run();

static ResourceDefinition CreateGraphApiDefinition(
    Uri endpoint,
    string projectPath,
    string? traceIngestEndpoint,
    string? metricIngestEndpoint,
    AspNetCoreProjectEnvironmentVariableValue changedEnvironmentVariable)
{
    var environmentVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["CLOUDSHELL_TRACE_INGEST_ENDPOINT"] = traceIngestEndpoint ?? string.Empty,
        ["CLOUDSHELL_METRIC_INGEST_ENDPOINT"] = metricIngestEndpoint ?? string.Empty,
        ["OTEL_SERVICE_NAME"] = "graph-project-reference-api",
        [changedEnvironmentVariable.Name] = changedEnvironmentVariable.Value ?? string.Empty
    };

    return new ResourceDefinition(
        "graph-project-reference-api",
        AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
        ResourceId: "application.aspnet-core-project:graph-project-reference-api",
        ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
        DisplayName: "Graph Project Reference API",
        Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                projectPath,
            [AspNetCoreProjectResourceTypeProvider.Attributes.HotReload] =
                false,
            [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                false,
            [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                ResourceAttributeValue.FromObject(new[]
                {
                    new NetworkingEndpointRequestValue(
                        "http",
                        endpoint.Scheme,
                        Host: endpoint.Host,
                        Port: endpoint.Port,
                        Exposure: "Local")
                }),
            [AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables] =
                ResourceAttributeValue.FromObject(environmentVariables
                    .Select(pair => new AspNetCoreProjectEnvironmentVariableValue(
                        pair.Key,
                        pair.Value))
                    .ToArray())
        });
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

internal sealed record ProjectReferenceGraphEnvironmentVariableUpdate(
    string Name,
    string Value);

internal sealed record ProjectReferenceGraphApplyResponse(
    bool Committed,
    bool HasErrors,
    long BaseVersion,
    long ResultVersion,
    string Status,
    IReadOnlyList<ProjectReferenceGraphApplyDiagnosticResponse> Diagnostics)
{
    public static ProjectReferenceGraphApplyResponse FromResult(
        ResourceModelGraphDefinitionApplyResult result) =>
        new(
            result.IsCommitted,
            result.HasErrors,
            result.BaseVersion.Value,
            result.Commit.Version.Value,
            result.Commit.Summary.Status.ToString(),
            result.Diagnostics
                .Select(diagnostic => new ProjectReferenceGraphApplyDiagnosticResponse(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Target))
                .ToArray());
}

internal sealed record ProjectReferenceGraphApplyDiagnosticResponse(
    string Severity,
    string Code,
    string Message,
    string? Target);

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

internal sealed class AspNetCoreProjectResourceManagerObservabilityProvider :
    IResourceModelResourceManagerObservabilityProvider
{
    public ResourceObservability? GetObservability(
        CloudShell.ResourceDefinitions.Resource resource) =>
        resource.Type.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId
            ? new ResourceObservability(
                Logs: true,
                Traces: true,
                Metrics: true,
                ServiceName: resource.Name)
            : null;
}

internal sealed class AspNetCoreProjectResourceManagerLogProvider(
    IAspNetCoreProjectRuntimeOutputReader outputReader) : ILogProvider
{
    public string Id => "resource-model.aspnet-core-project.logs";

    public string DisplayName => "ASP.NET Core project logs";

    public IReadOnlyList<LogSource> GetLogSources() => [];

    public bool CanOpenLogSource(LogSource source) =>
        IsAspNetCoreProjectLogSource(source);

    public ValueTask<ILogSourceSession?> OpenLogSourceAsync(
        LogSource source,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult<ILogSourceSession?>(
            IsAspNetCoreProjectLogSource(source) && source.ResourceId is not null
                ? new AspNetCoreProjectResourceManagerLogSourceSession(outputReader, source)
                : null);
    }

    public Task<IReadOnlyList<LogEntry>> ReadLogSourceAsync(
        string logSourceId,
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<LogEntry>>([]);
    }

    private static bool IsAspNetCoreProjectLogSource(LogSource source) =>
        source.ResourceId?.StartsWith(
            $"{AspNetCoreProjectResourceTypeProvider.ResourceTypeId}:",
            StringComparison.OrdinalIgnoreCase) == true &&
        source.Kind is ResourceLogSourceKind.ProcessOutput
            or ResourceLogSourceKind.ProcessStdout
            or ResourceLogSourceKind.ProcessStderr;
}

internal sealed class AspNetCoreProjectResourceManagerLogSourceSession(
    IAspNetCoreProjectRuntimeOutputReader outputReader,
    LogSource source) : ILogSourceSession
{
    public string Id { get; } = Guid.NewGuid().ToString("N");

    public string SourceId => source.Id;

    public LogSourceSessionStatus Status { get; private set; } = LogSourceSessionStatus.Active;

    public Task<IReadOnlyList<LogEntry>> ReadAsync(
        int maxEntries = 200,
        DateTimeOffset? before = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<LogEntry> entries = source.ResourceId is null
            ? []
            : outputReader
                .ReadOutput(source.ResourceId, maxEntries, before)
                .Select(ToLogEntry)
                .ToArray();

        return Task.FromResult<IReadOnlyList<LogEntry>>(entries);
    }

    public async IAsyncEnumerable<LogEntry> StreamAsync(
        int initialEntries = 50,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var entries = await ReadAsync(initialEntries, cancellationToken: cancellationToken);
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return entry;
        }
    }

    public ValueTask DisposeAsync()
    {
        Status = LogSourceSessionStatus.Closed;
        return ValueTask.CompletedTask;
    }

    private static LogEntry ToLogEntry(AspNetCoreProjectRuntimeOutputEntry entry) =>
        new(
            entry.Timestamp,
            entry.Message,
            entry.Severity,
            entry.Stream);
}
