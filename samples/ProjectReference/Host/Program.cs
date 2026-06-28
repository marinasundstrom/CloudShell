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
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = builder.Configuration["Observability:MetricIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/metrics/ingest";
var apiEndpoint = builder.Configuration["ProjectReference:ApiEndpoint"]
    ?? "http://localhost:5229";
var frontendEndpoint = builder.Configuration["ProjectReference:FrontendEndpoint"]
    ?? "http://localhost:5230";
var apiEndpointUri = new Uri(apiEndpoint);
var frontendEndpointUri = new Uri(frontendEndpoint);
var apiProjectPath = Path.GetFullPath(
    "../Api/CloudShell.ProjectReferenceApi.csproj",
    builder.Environment.ContentRootPath);
var frontendProjectPath = Path.GetFullPath(
    "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
    builder.Environment.ContentRootPath);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
IResourceDefinitionBuilder apiResource = null!;
IResourceDefinitionBuilder frontendResource = null!;
cloudShell.DefineResources(resources =>
{
    apiResource = resources
        .AddAspNetCoreProject("project-reference-api", apiProjectPath)
        .WithDisplayName("Project Reference API")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithServiceDiscovery()
        .AddEndpointRequest(
            "http",
            apiEndpointUri.Scheme,
            host: apiEndpointUri.Host,
            port: apiEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "project-reference-api")
        .WithHttpHealthCheck(
            "/health",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");

    frontendResource = resources
        .AddAspNetCoreProject("project-reference-frontend", frontendProjectPath)
        .WithDisplayName("Project Reference Frontend")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithReference(
            apiResource,
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
        .AddEndpointRequest(
            "http",
            frontendEndpointUri.Scheme,
            host: frontendEndpointUri.Host,
            port: frontendEndpointUri.Port,
            exposure: "Local")
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint ?? string.Empty)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "project-reference-frontend")
        .WithHttpHealthCheck(
            "/healthz",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");
});
builder.Services
    .AddAspNetCoreProjectResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    resources.Declare(apiResource);
    resources.Declare(frontendResource);
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
        ProjectReferenceEnvironmentVariableUpdate update,
        ResourceModelGraphDefinitionApplyService applyService,
        CancellationToken cancellationToken) =>
    {
        if (!string.Equals(resourceId, apiResource.EffectiveResourceId, StringComparison.OrdinalIgnoreCase))
        {
            return Results.NotFound();
        }

        if (string.IsNullOrWhiteSpace(update.Name))
        {
            return Results.BadRequest("Environment variable name is required.");
        }

        var definition = CreateApiDefinition(
            apiEndpointUri,
            apiProjectPath,
            traceIngestEndpoint,
            metricIngestEndpoint,
            update.Name.Trim(),
            new AspNetCoreProjectEnvironmentVariableValue(update.Value));
        var result = await applyService.ApplyDefinitionsAsync(
            [definition],
            new ResourceGraphCommitContext(
                EnvironmentId: "project-reference",
                PrincipalId: "sample",
                Timestamp: DateTimeOffset.UtcNow),
            cancellationToken);

        return Results.Ok(ProjectReferenceApplyResponse.FromResult(result));
    });

app.Run();

static ResourceDefinition CreateApiDefinition(
    Uri endpoint,
    string projectPath,
    string? traceIngestEndpoint,
    string? metricIngestEndpoint,
    string changedEnvironmentVariableName,
    AspNetCoreProjectEnvironmentVariableValue changedEnvironmentVariable)
{
    var environmentVariables =
        new Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>(StringComparer.OrdinalIgnoreCase)
    {
        ["CLOUDSHELL_TRACE_INGEST_ENDPOINT"] = new(traceIngestEndpoint ?? string.Empty),
        ["CLOUDSHELL_METRIC_INGEST_ENDPOINT"] = new(metricIngestEndpoint ?? string.Empty),
        ["OTEL_SERVICE_NAME"] = new("project-reference-api"),
        [changedEnvironmentVariableName] = changedEnvironmentVariable
    };

    return new ResourceDefinition(
        "project-reference-api",
        AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
        ResourceId: "application.aspnet-core-project:project-reference-api",
        ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
        DisplayName: "Project Reference API",
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
                ResourceAttributeValue.FromObject(environmentVariables)
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

internal sealed record ProjectReferenceEnvironmentVariableUpdate(
    string Name,
    string Value);

internal sealed record ProjectReferenceApplyResponse(
    bool Committed,
    bool HasErrors,
    long BaseVersion,
    long ResultVersion,
    string Status,
    IReadOnlyList<ProjectReferenceApplyDiagnosticResponse> Diagnostics)
{
    public static ProjectReferenceApplyResponse FromResult(
        ResourceModelGraphDefinitionApplyResult result) =>
        new(
            result.IsCommitted,
            result.HasErrors,
            result.BaseVersion.Value,
            result.Commit.Version.Value,
            result.Commit.Summary.Status.ToString(),
            result.Diagnostics
                .Select(diagnostic => new ProjectReferenceApplyDiagnosticResponse(
                    diagnostic.Severity.ToString(),
                    diagnostic.Code,
                    diagnostic.Message,
                    diagnostic.Target))
                .ToArray());
}

internal sealed record ProjectReferenceApplyDiagnosticResponse(
    string Severity,
    string Code,
    string Message,
    string? Target);
