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

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddApplicationProvider()
    .UseLocalDevelopmentDefaults();

cloudShell.Resources(resources =>
{
    resources
        .AddSqlServer("sql-server")
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest");
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
