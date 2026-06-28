using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;

var builder = CloudShellApplication.CreateBuilder(args);

const string registryHost = "localhost";
var registryPort = builder.Configuration.GetValue("ContainerAppDeployment:RegistryPort", 5023);
string registryAddress = $"{registryHost}:{registryPort}";
const string sampleImage = "cloudshell/mock-api:20260608.1";
const string resourceGroupId = "container-app-deployment-poc";
const string dockerResourceId = "docker:sample";
const string registryResourceId = "docker.container:sample-registry";
const string containerAppResourceId = "application.container-app:sample-api";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
    {
        var docker = resources
            .AddDockerHost("sample")
            .WithResourceId(dockerResourceId)
            .WithRegistry(registryAddress);
        var registry = resources
            .AddDockerContainer("sample-registry")
            .WithResourceId(registryResourceId)
            .WithDisplayName("Sample Registry")
            .UseDockerHost(docker)
            .WithImage("registry:2")
            .WithRegistry(registryAddress);
        resources
            .AddContainerApplication("sample-api")
            .WithResourceId(containerAppResourceId)
            .WithDisplayName("Sample API")
            .UseDockerHost(docker)
            .DependsOn(registry)
            .WithImage(sampleImage)
            .WithRegistry(registryAddress);
    });
if (builder.Configuration.GetValue("ContainerAppDeployment:EnableDockerRuntime", false))
{
    builder.Services
        .AddSingleton<IDockerContainerRuntimeHandler, ContainerAppDeploymentDockerContainerRuntimeHandler>()
        .AddSingleton<IResourceOrchestrationDescriptorProvider, ContainerAppDeploymentDockerContainerOrchestrationDescriptorProvider>();
}

builder.Services
    .AddSingleton<IContainerAppDeploymentContainerApplicationRuntimeBridge, ContainerAppDeploymentContainerApplicationRuntimeBridge>()
    .AddSingleton<IContainerApplicationRuntimeHandler, ContainerAppDeploymentContainerApplicationRuntimeHandler>();

builder.Services
    .AddLocalContainerApplicationResourceTypes()
    .AddDockerContainerResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Container App Deployment POC",
        "Resources used by the ContainerAppDeployment sample.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, dockerResourceId)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, registryResourceId)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, containerAppResourceId)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
