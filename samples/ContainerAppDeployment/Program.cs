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

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
IResourceDefinitionBuilder dockerResource = null!;
IResourceDefinitionBuilder registryResource = null!;
IResourceDefinitionBuilder containerAppResource = null!;
cloudShell.DefineResources(resources =>
    {
        dockerResource = resources
            .AddDockerHost("sample")
            .WithRegistry(registryAddress);
        registryResource = resources
            .AddDockerContainer("sample-registry")
            .WithDisplayName("Sample Registry")
            .UseDockerHost(dockerResource)
            .WithImage("registry:2")
            .WithRegistry(registryAddress);
        containerAppResource = resources
            .AddContainerApplication("sample-api")
            .WithDisplayName("Sample API")
            .UseDockerHost(dockerResource)
            .DependsOn(registryResource)
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
        .Declare(dockerResource)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(registryResource)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
    resources
        .Declare(containerAppResource)
        .WithResourceGroup(resourceGroupId)
        .WithAutoStart(false);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
