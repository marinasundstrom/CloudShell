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
var sqlServerPort = builder.Configuration.GetValue<int?>("ContainerHost:SqlServer:Port") ?? 14334;

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Container Host POC",
    "Resources used by the ContainerHost sample.");
IResourceDefinitionBuilder storageResource = null!;
IResourceDefinitionBuilder volumeResource = null!;
cloudShell.DefineResources(resources =>
{
    storageResource = resources
        .AddStorage("local")
        .WithResourceGroup(resourceGroupId)
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    volumeResource = resources
        .AddCloudShellVolume("sql-data")
        .WithDisplayName("SQL Server Data")
        .WithResourceGroup(resourceGroupId)
        .UseStorage(storageResource)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();

    resources
        .AddSqlServer("sql-server")
        .WithDisplayName("SQL Server")
        .WithResourceGroup(resourceGroupId)
        .WithTcpEndpoint(
            host: "localhost",
            port: sqlServerPort)
        .MountVolume(volumeResource, "/var/opt/mssql");
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

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
