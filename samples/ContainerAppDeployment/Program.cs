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
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Container App Deployment POC",
    "Resources used by the ContainerAppDeployment sample.");
IResourceDefinitionBuilder dockerResource = null!;
IResourceDefinitionBuilder registryResource = null!;
cloudShell.DefineResources(resources =>
    {
        dockerResource = resources
            .AddDockerHost("sample")
            .WithResourceGroup(resourceGroupId)
            .WithAutoStart(false)
            .WithRegistry(registryAddress);
        registryResource = resources
            .AddDockerContainer("sample-registry")
            .WithDisplayName("Sample Registry")
            .WithResourceGroup(resourceGroupId)
            .WithAutoStart(false)
            .UseDockerHost(dockerResource)
            .WithImage("registry:2")
            .WithRegistry(registryAddress);
        resources
            .AddContainerApplication("sample-api")
            .WithDisplayName("Sample API")
            .WithResourceGroup(resourceGroupId)
            .WithAutoStart(false)
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

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
