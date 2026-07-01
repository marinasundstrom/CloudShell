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

builder.AddCloudShellControlPlaneApplication(
    configureBuiltInResourceModelProviders: null,
    configureControlPlane: controlPlane =>
    {
        controlPlane.DefineResources(resources =>
        {
            var resourceGroup = resources.AddResourceGroup(
                resourceGroupId,
                "Container Host",
                "Resources used by the ContainerHost sample.");

            var volumeResource = resources
                .AddVolume(
                    "sql-data",
                    path: "./Data/storage/sql-server")
                .WithDisplayName("SQL Server Data")
                .WithResourceGroup(resourceGroup);
            var containerHost = resources
                .GetContainerHost()
                .WithResourceGroup(resourceGroup);

            resources
                .AddSqlServer("sql-server")
                .WithDisplayName("SQL Server")
                .WithResourceGroup(resourceGroup)
                .UseContainerHost(containerHost)
                .WithTcpEndpoint(
                    host: "localhost",
                    port: sqlServerPort)
                .MountVolume(volumeResource, "/var/opt/mssql");
        });
    });
builder.Services
    .AddLocalSqlServerDockerRuntime(options =>
        options.AddServer(
            sqlServerResourceId,
            sqlServerContainerName,
            runtime =>
            {
                runtime.PasswordConfigurationKey = "ContainerHost:SqlServer:Password";
            }),
        descriptors => descriptors.AddResource(
            sqlServerResourceId,
            "container-host.sql-runtime.v1"));
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
