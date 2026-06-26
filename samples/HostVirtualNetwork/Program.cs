using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

const int targetPort = 5291;
const int virtualNetworkPort = 5290;
const string graphHostNetworkingResourceId = "networking:graph-host-local";
const string graphApiResourceId = "application.aspnet-core-project:graph-vnet-api";
const string graphNetworkResourceId = "network:graph-sample-vnet";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-host-local",
            LocalHostNetworkResourceTypeProvider.ResourceTypeId,
            ResourceId: graphHostNetworkingResourceId,
            ProviderId: LocalHostNetworkResourceTypeProvider.ProviderId),
        new ResourceGraphState(
            "graph-vnet-api",
            AspNetCoreProjectResourceTypeProvider.ResourceTypeId,
            ResourceId: graphApiResourceId,
            ProviderId: AspNetCoreProjectResourceTypeProvider.ProviderId,
            DisplayName: "Graph VNet API",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath] =
                    "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
                [AspNetCoreProjectResourceTypeProvider.Attributes.ProjectArguments] =
                    $"--urls http://localhost:{targetPort}",
                [AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings] =
                    false,
                [AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests] =
                    ResourceAttributeValue.FromObject(new[]
                    {
                        new NetworkingEndpointRequestValue(
                            "http",
                            "http",
                            Host: "localhost",
                            Port: targetPort,
                            Exposure: "Local")
                    })
            }),
        new ResourceGraphState(
            "graph-sample-vnet",
            VirtualNetworkResourceTypeProvider.ResourceTypeId,
            ResourceId: graphNetworkResourceId,
            ProviderId: VirtualNetworkResourceTypeProvider.ProviderId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphHostNetworkingResourceId,
                    typeId: LocalHostNetworkResourceTypeProvider.ResourceTypeId),
                ResourceReference.DependsOnResourceId(
                    graphApiResourceId,
                    typeId: AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [VirtualNetworkResourceTypeProvider.Attributes.IsDefault] = true,
                [VirtualNetworkResourceTypeProvider.Attributes.HostReadiness] = "providerRequired",
                [VirtualNetworkResourceTypeProvider.Attributes.MappingProviders] =
                    graphHostNetworkingResourceId
            })
    ])
    .AddLocalHostNetworkResourceType()
    .AddVirtualNetworkResourceType()
    .AddAspNetCoreProjectResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider();

cloudShell.Resources(resources =>
{
    var hostNetworking = resources.AddLocalHostNetworking();

    var api = resources
        .AddAspNetCoreProject(
            "vnet-api",
            "../CloudShell.ExampleWebApi/CloudShell.ExampleWebApi.csproj",
            endpoint: $"http://localhost:{targetPort}")
        .WithAutoStart(false);

    var network = resources
        .AddVirtualNetwork(
            "sample-vnet",
            isDefault: true);

    var ingress = network.AddHttpEndpoint(
        "localhost",
        virtualNetworkPort,
        "api-public",
        ResourceExposureScope.Public);

    network.MapEndpoint(
        ingress,
        new ResourceEndpointReference(api.ResourceId, "http"),
        hostNetworking,
        "mapping:api-public",
        "API public ingress");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphHostNetworkingResourceId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphApiResourceId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
