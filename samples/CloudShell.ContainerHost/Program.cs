using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ContainerHost;
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
using System.Text.Json;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;

var builder = CloudShellApplication.CreateBuilder(args);

const string graphResourceGroupId = "container-host-graph-poc";
const string graphStorageResourceId = "cloudshell.storage:graph-local";
const string graphVolumeResourceId = "cloudshell.volume:graph-sql-data";
const string graphSqlServerResourceId = "application.sql-server:graph-sql-server";

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
builder.Services
    .AddInMemoryResourceModelGraph(
    [
        new ResourceGraphState(
            "graph-local",
            StorageResourceTypeProvider.ResourceTypeId,
            ResourceId: graphStorageResourceId,
            ProviderId: StorageResourceTypeProvider.ProviderId,
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [StorageResourceTypeProvider.Attributes.Provider] = "local",
                [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                [StorageResourceTypeProvider.Attributes.Location] = "./Data/storage"
            }),
        new ResourceGraphState(
            "graph-sql-data",
            CloudShellVolumeResourceTypeProvider.ResourceTypeId,
            ResourceId: graphVolumeResourceId,
            ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
            DisplayName: "Graph SQL Server Data",
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    graphStorageResourceId,
                    typeId: StorageResourceTypeProvider.ResourceTypeId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
            {
                [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "local",
                [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "sql-server",
                [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = true
            }),
        new ResourceGraphState(
            "graph-sql-server",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ResourceId: graphSqlServerResourceId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            DisplayName: "Graph SQL Server",
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new(graphVolumeResourceId, "/var/opt/mssql")
                    ]))
            })
    ])
    .AddStorageResourceType()
    .AddCloudShellVolumeResourceType()
    .AddSqlServerResourceType()
    .AddResourceModelGraphServices()
    .AddReferenceProviderResourceManagerProjections()
    .AddResourceModelGraphProcedureProvider(
        ResourceModelResourceProvider.DefaultProviderId,
        "Resource model");

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .UseLocalDevelopmentDefaults();

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Container Host graph POC",
        "Side-by-side graph-backed resources used while porting the ContainerHost sample.");

    ContainerHostSampleResources.AddResources(resources);

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphStorageResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphVolumeResourceId)
        .WithResourceGroup(graphResourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, graphSqlServerResourceId)
        .WithResourceGroup(graphResourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
