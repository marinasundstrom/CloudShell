using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Providers.Applications;

var builder = WebApplication.CreateBuilder(args);

var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];

var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider(options =>
    {
        options.OtlpEndpoint = otlpEndpoint;
        options.OtlpProtocol = otlpProtocol;
    });

cloudShell.Resources(resources =>
{
    var api = resources.AddAspNetCoreProject(
        "application:project-reference-api",
        "Project Reference API",
        "../Api/CloudShell.ProjectReferenceApi.csproj")
        .WithHttpHealthCheck("/health")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol);

    resources
        .AddAspNetCoreProject(
            "application:project-reference-frontend",
            "Project Reference Frontend",
            "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
            endpoint: "http://localhost:5218")
        .WithHttpHealthCheck("/healthz")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithOtlpExporter(otlpEndpoint, otlpProtocol)
        .WithReference(api)
        .DependsOn(api);
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
