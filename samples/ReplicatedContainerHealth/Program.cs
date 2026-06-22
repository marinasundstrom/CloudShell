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

const string registryHost = "localhost";
var registryPort = builder.Configuration.GetValue("ReplicatedContainerHealth:RegistryPort", 5024);
string registryAddress = $"{registryHost}:{registryPort.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
const string registryResourceId = "docker:container:replicated-health-registry";
const string sampleImageTag = "20260622.1";

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
        .WithRegistry(registryAddress)
        .Persist(overwrite: true);

    var registry = docker
        .AddDockerContainer(
            registryResourceId,
            "registry:2")
        .WithEndpoint(
            "http",
            targetPort: 5000,
            port: registryPort,
            protocol: "http",
            exposure: ResourceExposureScope.Public)
        .WithHttpHealthCheck("/v2/", "http")
        .WithAutoStart(false)
        .Persist(overwrite: true);

    resources
        .AddAspNetCoreProject(
            "api",
            "Api/CloudShell.ReplicatedContainerHealth.Api.csproj")
        .AsContainer(registry: registryAddress, replicas: 3, tag: sampleImageTag)
        .WithEndpointPort(
            "http",
            targetPort: 8080,
            port: 5092,
            protocol: "http",
            exposure: ResourceExposureScope.Local)
        .WithHttpHealthCheck("/health", "http")
        .WithHttpProbe(ResourceProbeType.Liveness, "/alive", "http", "alive")
        .WithContainerHost(docker)
        .DependsOn(registry)
        .WithReference(registry)
        .WithAutoStart(false)
        .Persist(overwrite: true);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
