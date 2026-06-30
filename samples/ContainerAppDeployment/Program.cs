using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ResourceModel;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
using CloudShell.ControlPlane.ResourceModel;

var builder = CloudShellApplication.CreateBuilder(args);

const string registryHost = "localhost";
var registryPort = builder.Configuration.GetValue("ContainerAppDeployment:RegistryPort", 5023);
string registryAddress = $"{registryHost}:{registryPort}";
const string sampleImage = "cloudshell/mock-api:20260608.1";
const string resourceGroupId = "container-app-deployment";
const string registryResourceId = "docker.container:sample-registry";
const string registryContainerName = "cloudshell-container-app-deployment-registry";
const string sampleApiResourceId = "application.container-app:sample-api";

var cloudShell = builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Container App Deployment",
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
        .AddLocalDockerContainerRuntime(options =>
            options.AddContainer(
                registryResourceId,
                registryContainerName,
                runtime => runtime.TargetPort = 5000),
            descriptors => descriptors.AddResource(
                registryResourceId,
                "container-app-deployment.registry-runtime.v1"));
}

builder.Services
    .AddLocalContainerApplicationResourceTypes()
    .AddDeferredContainerApplicationRuntime(options =>
        options.AddResource(sampleApiResourceId))
    .AddDockerContainerResourceType();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddBuiltInProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
