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

const string resourceGroupId = "container-host-poc";
const string storageResourceId = "cloudshell.storage:local";
const string volumeResourceId = "cloudshell.volume:sql-data";
const string sqlServerResourceId = "application.sql-server:sql-server";
var sqlServerPort = builder.Configuration.GetValue<int?>("ContainerHost:SqlServer:Port") ?? 14334;

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.DefineResources(resources =>
{
    var storage = resources
        .AddStorage("local")
        .WithResourceId(storageResourceId)
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    var volume = resources
        .AddCloudShellVolume("sql-data")
        .WithResourceId(volumeResourceId)
        .WithDisplayName("SQL Server Data")
        .UseStorage(storage)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();

    resources
        .AddSqlServer("sql-server")
        .WithResourceId(sqlServerResourceId)
        .WithDisplayName("SQL Server")
        .AddEndpointRequest(
            "tds",
            "tcp",
            targetPort: 1433,
            host: "localhost",
            port: sqlServerPort,
            exposure: "Local")
        .MountVolume(volume, "/var/opt/mssql");
});
builder.Services
    .AddSingleton<IContainerHostDockerCommandRunner, ProcessContainerHostDockerCommandRunner>()
    .AddSingleton<IContainerHostSqlServerRuntimeBridge, ContainerHostSqlServerDockerBridge>()
    .AddSingleton<ISqlServerRuntimeHandler, ContainerHostSqlServerRuntimeHandler>()
    .AddSingleton<IResourceOrchestrationDescriptorProvider, ContainerHostSqlServerOrchestrationDescriptorProvider>()
    .AddStorageBackedSqlServerResourceTypes();
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddApplicationResourceManagerUi();

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Container Host POC",
        "Resources used by the ContainerHost sample.");

    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, storageResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, volumeResourceId)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(ResourceModelResourceProvider.DefaultProviderId, sqlServerResourceId)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
