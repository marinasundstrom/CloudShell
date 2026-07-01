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

builder.AddCloudShellControlPlaneApplication(
    options =>
    {
        options.IncludeDefaultEnvironmentResources = false;
    },
    controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var resourceGroup = resources.AddResourceGroup(
                resourceGroupId,
                "Container App Deployment",
                "Resources used by the ContainerAppDeployment sample.");

            var dockerResource = resources
                .AddDockerHost("sample")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .WithRegistry(registryAddress);
            var registryResource = resources
                .AddDockerContainer("sample-registry")
                .WithDisplayName("Sample Registry")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .UseDockerHost(dockerResource)
                .WithImage("registry:2")
                .WithRegistry(registryAddress);
            resources
                .AddContainerApplication("sample-api")
                .WithDisplayName("Sample API")
                .WithResourceGroup(resourceGroup)
                .WithAutoStart(false)
                .UseDockerHost(dockerResource)
                .DependsOn(registryResource)
                .WithImage(sampleImage)
                .WithRegistry(registryAddress);
        });
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
    .AddDeferredContainerApplicationRuntime(options =>
        options.AddResource(sampleApiResourceId));
builder.AddCloudShellUi(ui =>
{
    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<ObservabilityExtension>();
    ui.AddBuiltInProviderResourceManagerUi();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
