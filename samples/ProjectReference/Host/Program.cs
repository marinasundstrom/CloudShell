using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShellEndpoint = ResolveCloudShellEndpoint(builder.Configuration);
var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"]
    ?? cloudShellEndpoint;
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
var traceIngestEndpoint = builder.Configuration["Observability:TraceIngestEndpoint"]
    ?? $"{cloudShellEndpoint}/api/control-plane/v1/traces/ingest";
var frontendEndpoint = builder.Configuration["ProjectReference:FrontendEndpoint"]
    ?? "http://localhost:5218";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

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
        "application:project-reference-api",
        "../Api/CloudShell.ProjectReferenceApi.csproj")
        .WithDisplayName("Project Reference API")
        .WithHttpHealthCheck("/health")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty);

    resources
        .AddAspNetCoreProject(
        "application:project-reference-frontend",
        "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
        endpoint: frontendEndpoint)
        .WithDisplayName("Project Reference Frontend")
        .WithHttpHealthCheck("/healthz")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithEnvironment("CLOUDSHELL_TRACE_INGEST_ENDPOINT", traceIngestEndpoint ?? string.Empty)
        .WithServiceDiscovery()
        .WithReference(api)
        .DependsOn(api);
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
