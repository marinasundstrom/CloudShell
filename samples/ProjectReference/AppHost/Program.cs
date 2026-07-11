using CloudShell.AppHost.Launcher;
using CloudShell.ControlPlane.Providers;

var app = CloudShellDistributedApplication
    .CreateBuilder("project-reference", args)
    .WithMetadata("cloudshell.source", "csharp")
    .WithMetadata("cloudshell.sample", "ProjectReference");

var cloudShellEndpoint = Environment.GetEnvironmentVariable("CLOUDSHELL_CONTROL_PLANE_URL")?.TrimEnd('/') ??
    app.Configuration["CloudShell:Launcher:ControlPlaneUrl"]?.TrimEnd('/') ??
    "http://127.0.0.1:5104";
var traceIngestEndpoint = app.Configuration["ProjectReference:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var metricIngestEndpoint = app.Configuration["ProjectReference:MetricIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/metrics/ingest";
var apiEndpoint = new Uri(app.Configuration["ProjectReference:ApiEndpoint"]
    ?? "http://localhost:5229");
var frontendEndpoint = new Uri(app.Configuration["ProjectReference:FrontendEndpoint"]
    ?? "http://localhost:5230");
var apiProjectPath = app.ResolvePath("..", "Api", "CloudShell.ProjectReferenceApi.csproj");
var frontendProjectPath = app.ResolvePath("..", "Frontend", "CloudShell.ProjectReferenceFrontend.csproj");

app.DefineResources(resources =>
{
    var apiResource = resources
        .AddDotnetApp("project-reference-api", apiProjectPath)
        .WithDisplayName("Project Reference API")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithServiceDiscovery()
        .WithHttpEndpoint(
            host: apiEndpoint.Host,
            port: apiEndpoint.Port)
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint)
        .WithEnvironmentVariable(
            "OTEL_SERVICE_NAME",
            "project-reference-api")
        .WithHttpHealthCheck(
            "/health",
            endpointName: "http")
        .WithHttpLivenessCheck(
            "/alive",
            endpointName: "http");

    resources
        .AddDotnetApp("project-reference-frontend", frontendProjectPath)
        .WithDisplayName("Project Reference Frontend")
        .WithHotReload(false)
        .UseLaunchSettings(false)
        .WithReference(apiResource)
        .WithHttpEndpoint(
            host: frontendEndpoint.Host,
            port: frontendEndpoint.Port)
        .WithEnvironmentVariable(
            "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
            traceIngestEndpoint)
        .WithEnvironmentVariable(
            "CLOUDSHELL_METRIC_INGEST_ENDPOINT",
            metricIngestEndpoint)
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

return await app.LaunchAsync();
