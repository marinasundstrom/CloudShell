using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ResourceDefinitions;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

const string graphNetworkResourceId = "network:graph-split-sample";

var controlPlane = builder.AddCloudShellControlPlane();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-split-sample",
            NetworkResourceTypeProvider.ResourceTypeId,
            ResourceId: graphNetworkResourceId,
            ProviderId: NetworkResourceTypeProvider.ProviderId,
            DisplayName: "Graph Split Sample Network",
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [NetworkResourceTypeProvider.Attributes.NetworkKind] = "Logical",
                [NetworkResourceTypeProvider.Attributes.HostReadiness] = "logicalOnly"
            })
    ])
    .AddNetworkResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

controlPlane.Resources(resources =>
{
    resources
        .AddNetwork("split-sample", isDefault: true)
        .WithDisplayName("Split Sample Network")
        .Persist();

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphNetworkResourceId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
app.MapCloudShellControlPlane();

app.Run();
