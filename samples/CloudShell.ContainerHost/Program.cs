using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ContainerHost;
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

const string resourceGroupId = "container-host";
const string sqlServerResourceId = "application.sql-server:sql-server";
const string sqlServerContainerName = "cloudshell-container-host-sql-server";
var sqlServerPort = builder.Configuration.GetValue<int?>("ContainerHost:SqlServer:Port") ?? 14334;

var cloudShell = builder.AddCloudShell();
cloudShell.AddResourceGroup(
    resourceGroupId,
    "Container Host",
    "Resources used by the ContainerHost sample.");
IResourceDefinitionBuilder volumeResource = null!;
cloudShell.DefineResources(resources =>
{
    volumeResource = resources
        .AddVolume(
            "sql-data",
            path: "./Data/storage/sql-server")
        .WithDisplayName("SQL Server Data")
        .WithResourceGroup(resourceGroupId);
    var containerHost = resources
        .DefaultContainerHost()
        .WithResourceGroup(resourceGroupId);

    resources
        .AddSqlServer("sql-server")
        .WithDisplayName("SQL Server")
        .WithResourceGroup(resourceGroupId)
        .UseContainerHost(containerHost)
        .WithTcpEndpoint(
            host: "localhost",
            port: sqlServerPort)
        .MountVolume(volumeResource, "/var/opt/mssql");
});
builder.Services
    .AddSingleton<IResourceOrchestrationDescriptorProvider, ContainerHostSqlServerOrchestrationDescriptorProvider>()
    .AddStorageBackedSqlServerResourceTypes()
    .AddLocalSqlServerDockerRuntime(options =>
        options.AddServer(
            sqlServerResourceId,
            sqlServerContainerName,
            runtime =>
            {
                runtime.PasswordConfigurationKey = "ContainerHost:SqlServer:Password";
            }));
cloudShell.UseResourceGraphIntegration();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>();
cloudShell.AddBuiltInProviderResourceManagerUi();

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
