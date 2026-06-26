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
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

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

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
if (builder.Configuration.GetValue("ContainerAppDeployment:EnableGraphDockerRuntime", false))
{
    builder.Services
        .AddSingleton<IDockerContainerRuntimeHandler, ContainerAppDeploymentGraphDockerContainerRuntimeHandler>();
}

builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-sample",
            DockerHostResourceTypeProvider.ResourceTypeId,
            ResourceId: graphDockerResourceId,
            ProviderId: DockerHostResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [DockerHostResourceTypeProvider.Attributes.Registry] =
                    registryAddress
            }),
        new ResourceGraphState(
            "graph-sample-registry",
            DockerContainerResourceTypeProvider.ResourceTypeId,
            ResourceId: graphRegistryResourceId,
            ProviderId: DockerContainerResourceTypeProvider.ProviderId,
            DisplayName: "Graph Sample Registry",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [DockerContainerResourceTypeProvider.Attributes.ContainerImage] =
                    "registry:2",
                [DockerContainerResourceTypeProvider.Attributes.ContainerRegistry] =
                    registryAddress,
                [DockerContainerResourceTypeProvider.Attributes.EndpointCount] =
                    1
            }),
        new ResourceGraphState(
            "graph-sample-api",
            ContainerApplicationResourceTypeProvider.ResourceTypeId,
            ResourceId: graphContainerAppResourceId,
            ProviderId: ContainerApplicationResourceTypeProvider.ProviderId,
            DisplayName: "Graph Sample API",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphDockerResourceId,
                    typeId: DockerHostResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphRegistryResourceId,
                    typeId: DockerContainerResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerImage] =
                    sampleImage,
                [ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry] =
                    registryAddress
            })
    ])
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
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .AddDockerProvider()
    .UseLocalDevelopmentDefaults(options =>
    {
        options.Registry = registryAddress;
    });

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Container App Deployment graph POC",
        "Side-by-side graph-backed resources used while porting the ContainerAppDeployment sample.");

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
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
