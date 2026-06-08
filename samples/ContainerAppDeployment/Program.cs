using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;

var builder = WebApplication.CreateBuilder(args);

const string registryAddress = "localhost:5000";
const string registryResourceId = "docker:container:sample-registry";
const string containerAppResourceId = "application:sample-api";
const string sampleImage = "cloudshell/mock-api:20260608.1";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddDockerProvider()
    .UseLocalDevelopmentDefaults(options =>
    {
        options.Registry = registryAddress;
    });

cloudShell.Resources(resources =>
{
    var docker = resources
        .AddDocker("docker:sample", "Sample Docker Environment")
        .WithRegistry(registryAddress)
        .Persist(overwrite: true);

    var registry = docker
        .AddDockerContainer(
            registryResourceId,
            "Local Registry",
            "registry:2",
            [
                new ResourceEndpoint(
                    "http",
                    "http://localhost:5000",
                    "http",
                    true)
            ])
        .WithEndpoint(new ResourceEndpoint(
            "registry",
            "http://localhost:5000",
            "http",
            true))
        .WithHttpHealthCheck("/v2/", "http")
        .WithAutoStart(false)
        .Persist(overwrite: true);

    resources
        .AddContainerApplication(
            containerAppResourceId,
            "Sample API",
            sampleImage,
            endpoints:
            [
                new ResourceEndpoint(
                    "http",
                    "http://localhost:5088",
                    "http",
                    true)
            ],
            registry: registryAddress)
        .WithContainerEngine(docker)
        .DependsOn(registry)
        .WithEnvironment("SAMPLE_REGISTRY", registryAddress)
        .WithAutoStart(false)
        .Persist(overwrite: true);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
