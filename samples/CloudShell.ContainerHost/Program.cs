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

var builder = CloudShellApplication.CreateBuilder(args);

const string graphResourceGroupId = "container-host-graph-poc";
const string graphStorageResourceId = "cloudshell.storage:graph-local";
const string graphVolumeResourceId = "cloudshell.volume:graph-sql-data";
const string graphSqlServerResourceId = "application.sql-server:graph-sql-server";
var graphSqlServerPort = builder.Configuration.GetValue<int?>("ContainerHost:GraphSqlServer:Port") ?? 14334;
var graphOnly = builder.Configuration.GetValue("ContainerHost:GraphOnly", false);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    var graphStorage = resources
        .AddStorage("graph-local")
        .WithResourceId(graphStorageResourceId)
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    var graphVolume = resources
        .AddCloudShellVolume("graph-sql-data")
        .WithResourceId(graphVolumeResourceId)
        .WithDisplayName("Graph SQL Server Data")
        .UseStorage(graphStorage)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();

    resources
        .AddSqlServer("graph-sql-server")
        .WithResourceId(graphSqlServerResourceId)
        .WithDisplayName("Graph SQL Server")
        .AddEndpointRequest(
            "tds",
            "tcp",
            targetPort: 1433,
            host: "localhost",
            port: graphSqlServerPort,
            exposure: "Local")
        .MountVolume(graphVolume, "/var/opt/mssql");
});
builder.Services
    .AddSingleton<IContainerHostDockerCommandRunner, ProcessContainerHostDockerCommandRunner>()
    .AddSingleton<IContainerHostGraphSqlServerRuntimeBridge, ContainerHostGraphSqlServerDockerBridge>()
    .AddSingleton<ISqlServerRuntimeHandler, ContainerHostGraphSqlServerRuntimeHandler>()
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
    .AddExtension<ObservabilityExtension>();

if (!graphOnly)
{
    cloudShell
        .AddApplicationProvider()
        .UseLocalDevelopmentDefaults();
}

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        graphResourceGroupId,
        "Container Host graph POC",
        "Side-by-side graph-backed resources used while porting the ContainerHost sample.");

    if (!graphOnly)
    {
        ContainerHostSampleResources.AddResources(resources);
    }

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
