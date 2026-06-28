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
IResourceDefinitionBuilder storageResource = null!;
IResourceDefinitionBuilder volumeResource = null!;
IResourceDefinitionBuilder sqlServerResource = null!;
cloudShell.DefineResources(resources =>
{
    storageResource = resources
        .AddStorage("local")
        .WithProvider("local")
        .WithMedium("FileSystem")
        .WithLocation("./Data/storage");
    volumeResource = resources
        .AddCloudShellVolume("sql-data")
        .WithDisplayName("SQL Server Data")
        .UseStorage(storageResource)
        .WithProvider("local")
        .WithStorageMedium("FileSystem")
        .WithSubPath("sql-server")
        .WithAccessMode("ReadWriteOnce")
        .WithPersistent();

    sqlServerResource = resources
        .AddSqlServer("sql-server")
        .WithDisplayName("SQL Server")
        .AddEndpointRequest(
            "tds",
            "tcp",
            targetPort: 1433,
            host: "localhost",
            port: sqlServerPort,
            exposure: "Local")
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

cloudShell.Resources(resources =>
{
    resources.AddResourceGroup(
        resourceGroupId,
        "Container Host POC",
        "Resources used by the ContainerHost sample.");

    resources
        .Declare(storageResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(volumeResource)
        .WithResourceGroup(resourceGroupId);
    resources
        .Declare(sqlServerResource)
        .WithResourceGroup(resourceGroupId);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
