using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string registryHost = "localhost";
var registryPort = builder.Configuration.GetValue("ContainerAppDeployment:RegistryPort", 5023);
string registryAddress = $"{registryHost}:{registryPort}";
const string registryResourceId = "docker:container:sample-registry";
const string containerAppResourceId = "application:sample-api";
const string sampleImage = "cloudshell/mock-api:20260608.1";
const string graphResourceGroupId = "container-app-deployment-graph-poc";
const string graphDockerResourceId = "docker:graph-sample";
const string graphRegistryResourceId = "docker.container:graph-sample-registry";
const string graphContainerAppResourceId = "application.container-app:graph-sample-api";
var graphOnly = builder.Configuration.GetValue("ContainerAppDeployment:GraphOnly", true);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
    {
        var graphDocker = resources
            .AddDockerHost("graph-sample")
            .WithResourceId(graphDockerResourceId)
            .WithRegistry(registryAddress);
        var graphRegistry = resources
            .AddDockerContainer("graph-sample-registry")
            .WithResourceId(graphRegistryResourceId)
            .WithDisplayName("Graph Sample Registry")
            .UseDockerHost(graphDocker)
            .WithImage("registry:2")
            .WithRegistry(registryAddress);
        resources
            .AddContainerApplication("graph-sample-api")
            .WithResourceId(graphContainerAppResourceId)
            .WithDisplayName("Graph Sample API")
            .UseDockerHost(graphDocker)
            .DependsOn(graphRegistry)
            .WithImage(sampleImage)
            .WithRegistry(registryAddress);
    });
if (builder.Configuration.GetValue("ContainerAppDeployment:EnableGraphDockerRuntime", false))
{
    builder.Services
        .AddSingleton<IDockerContainerRuntimeHandler, ContainerAppDeploymentGraphDockerContainerRuntimeHandler>()
        .AddSingleton<IResourceOrchestrationDescriptorProvider, ContainerAppDeploymentGraphDockerContainerOrchestrationDescriptorProvider>();
}

builder.Services
    .AddSingleton<
        IContainerAppDeploymentGraphContainerApplicationRuntimeBridge>(
        serviceProvider => graphOnly
            ? new ContainerAppDeploymentGraphOnlyContainerApplicationRuntimeBridge()
            : new ContainerAppDeploymentGraphResourceManagerContainerApplicationBridge(
                serviceProvider.GetRequiredService<IServiceScopeFactory>()))
    .AddSingleton<IContainerApplicationRuntimeHandler, ContainerAppDeploymentGraphContainerApplicationRuntimeHandler>();

builder.Services
    .AddDockerHostResourceType()
    .AddDockerContainerResourceType()
    .AddContainerApplicationResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell
        .AddApplicationProvider()
        .AddDockerProvider()
        .UseLocalDevelopmentDefaults(options =>
        {
            options.Registry = registryAddress;
        });
}

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Container App Deployment graph POC",
        "Side-by-side graph-backed resources used while porting the ContainerAppDeployment sample.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphDockerResourceId)
        .WithResourceGroup(graphResourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphRegistryResourceId)
        .WithResourceGroup(graphResourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphContainerAppResourceId)
        .WithResourceGroup(graphResourceGroupId)
        .WithAutoStart(false);

    if (!graphOnly)
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
            .AddContainerApplication(
                containerAppResourceId,
                sampleImage,
                registry: registryAddress)
            .WithEndpoint(
                "http",
                targetPort: 80,
                port: 5088,
                protocol: "http",
                exposure: ResourceExposureScope.Public)
            .WithContainerHost(docker)
            .DependsOn(registry)
            .WithReference(registry)
            .WithServiceDiscovery()
            .WithEnvironment("SAMPLE_REGISTRY", registryAddress)
            .WithAutoStart(false)
            .Persist(overwrite: true);
    }
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
