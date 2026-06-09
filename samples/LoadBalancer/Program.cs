using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.Providers.Traefik;

var builder = WebApplication.CreateBuilder(args);

var dynamicConfigurationDirectory = Path.Combine(
    builder.Environment.ContentRootPath,
    "Data",
    "traefik");

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddDockerProvider()
    .AddTraefikProvider(options =>
    {
        options.DynamicConfigurationDirectory = dynamicConfigurationDirectory;
    });

cloudShell.Resources(resources =>
{
    var dockerHost = resources
        .AddDocker("docker:sample-host", "Sample Container Host")
        .Persist(overwrite: true);

    var webApp = resources
        .AddContainerApplication(
            "application:web",
            "Web App",
            "cloudshell/mock-web:1.0.0")
        .WithEndpoint(
            "http",
            targetPort: 8080,
            port: 5080,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithContainerEngine(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var apiService = resources
        .AddContainerApplication(
            "application:api",
            "API Service",
            "cloudshell/mock-api:1.0.0")
        .WithEndpoint(
            "http",
            targetPort: 5000,
            port: 5081,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithContainerEngine(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var postgres = resources
        .AddContainerApplication(
            "application:postgres",
            "Postgres Replica Set",
            "cloudshell/mock-postgres:1.0.0")
        .WithEndpoint(
            "postgres",
            targetPort: 5432,
            port: 55432,
            protocol: "tcp",
            exposure: ResourceExposureScope.Local)
        .WithContainerEngine(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var lb = resources
        .AddLoadBalancer("public")
        .UseProvider("traefik")
        .UseHost(dockerHost)
        .ExposeHttp(80)
        .ExposeHttps(443)
        .ExposeTcp(5432, "postgres");

    lb.MapHost("app.local", webApp, endpoint: "http");
    lb.MapPath("api.local", "/v1", apiService, endpoint: "http");
    lb.MapTcp(5432, postgres, endpoint: "postgres");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
