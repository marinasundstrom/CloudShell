using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Providers.Applications;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider();

cloudShell.Resources(resources =>
{
    var api = resources.AddAspNetCoreProject(
        "application:project-reference-api",
        "Project Reference API",
        "../Api/CloudShell.ProjectReferenceApi.csproj")
        .WithHttpHealthCheck("/health")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive");

    resources
        .AddAspNetCoreProject(
            "application:project-reference-frontend",
            "Project Reference Frontend",
            "../Frontend/CloudShell.ProjectReferenceFrontend.csproj",
            endpoint: "http://localhost:5218")
        .WithHttpHealthCheck("/healthz")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive")
        .WithReference(api)
        .DependsOn(api);
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
