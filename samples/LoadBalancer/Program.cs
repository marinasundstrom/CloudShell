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

var builder = WebApplication.CreateBuilder(SampleHostSettings.CreateWebApplicationOptions(args));

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
        options.ManageRuntimeContainer = !string.Equals(
            Environment.GetEnvironmentVariable("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME"),
            "true",
            StringComparison.OrdinalIgnoreCase);
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
            "nginx:1.27-alpine")
        .WithEndpoint(
            "http",
            targetPort: 80,
            port: 5080,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var apiService = resources
        .AddContainerApplication(
            "application:api",
            "API Service",
            "traefik/whoami:v1.10")
        .WithEndpoint(
            "http",
            targetPort: 80,
            port: 5081,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithReplicas(3)
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var postgres = resources
        .AddContainerApplication(
            "application:postgres",
            "Postgres Replica Set",
            "postgres:16-alpine")
        .WithEndpoint(
            "postgres",
            targetPort: 5432,
            port: 55432,
            protocol: "tcp",
            exposure: ResourceExposureScope.Local)
        .WithEnvironment("POSTGRES_PASSWORD", "cloudshell")
        .WithEnvironment("POSTGRES_DB", "cloudshell")
        .WithContainerHost(dockerHost)
        .WithAutoStart(false)
        .Persist(overwrite: true);

    var lb = resources
        .AddLoadBalancer("public")
        .UseProvider("traefik")
        .UseHost(dockerHost)
        .ExposeHttp(80)
        .ExposeHttps(443)
        .ExposeTcp(5432, "postgres");

    lb.MapHost("app.local", webApp, port: 80);
    lb.MapPath("api.local", "/v1", apiService, port: 80);
    lb.MapTcp(5432, postgres, targetPort: 5432);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
