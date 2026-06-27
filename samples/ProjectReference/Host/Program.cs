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
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

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
var graphFrontendEndpoint = builder.Configuration["ProjectReference:GraphFrontendEndpoint"]
    ?? "http://localhost:5230";
var graphApiEndpointUri = new Uri(graphApiEndpoint);
var graphFrontendEndpointUri = new Uri(graphFrontendEndpoint);
var graphApiResourceId = "application.aspnet-core-project:graph-project-reference-api";
var graphFrontendResourceId = "application.aspnet-core-project:graph-project-reference-frontend";
var graphOnly = builder.Configuration.GetValue("ProjectReference:GraphOnly", false);
var graphApiProjectPath = Path.GetFullPath(
    "../Api/CloudShell.ProjectReferenceApi.csproj",
    builder.Environment.ContentRootPath);
var graphFrontendProjectPath = Path.GetFullPath(
    "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    resources
        .AddAspNetCoreProject("graph-project-reference-api", graphApiProjectPath)
        .WithDisplayName("Graph Project Reference API")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithServiceDiscoveryName("project-reference-api")
        .AddEndpointRequest(
            "http",
            graphApiEndpointUri.Scheme,
            host: graphApiEndpointUri.Host,
            port: graphApiEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "graph-project-reference-api")
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/health",
            endpointName: "http"))
        .AddHealthCheck(ResourceHealthCheckDefinition.HttpLiveness(
            "/alive",
            endpointName: "http",
            name: "alive"));

    resources
        .AddAspNetCoreProject("graph-project-reference-frontend", graphFrontendProjectPath)
        .WithResourceId(graphFrontendResourceId)
        .WithDisplayName("Graph Project Reference Frontend")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithReference(
            graphApiResourceId,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AddEndpointRequest(
            "http",
            graphFrontendEndpointUri.Scheme,
            host: graphFrontendEndpointUri.Host,
            port: graphFrontendEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "graph-project-reference-frontend")
        .AddHealthCheck(ResourceHealthCheckDefinition.Http(
            "/healthz",
            endpointName: "http"))
        .AddHealthCheck(ResourceHealthCheckDefinition.HttpLiveness(
            "/alive",
            endpointName: "http",
            name: "alive"));
});
builder.Services
    .AddAspNetCoreProjectResourceType()
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
    cloudShell.AddApplicationProvider(options =>
    {
        options.OtlpEndpoint = otlpEndpoint;
        options.OtlpProtocol = otlpProtocol;
    });
}

cloudShell.Resources(resources =>
{
    if (!graphOnly)
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
    }

    resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphApiResourceId);
    resources.Declare(
        ResourceModelResourceProvider.DefaultProviderId,
        graphFrontendResourceId);
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
            [AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName] =
                "project-reference-api",
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
