using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddDockerProvider();

cloudShell.Resources(resources =>
{
    var docker = resources
        .AddDocker("sample")
        .Persist(overwrite: true);

    resources
        .AddContainerApplication(
            "api",
            "traefik/whoami:v1.10")
        .WithEndpoint(
            "http",
            targetPort: 80,
            port: 5092,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithReplicas(3)
        .WithHttpHealthCheck("/health", "http")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive", "http", "alive")
        .WithContainerHost(docker)
        .WithAutoStart(false)
        .Persist(overwrite: true);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
