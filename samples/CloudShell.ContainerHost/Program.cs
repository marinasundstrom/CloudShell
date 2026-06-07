using CloudShell.Abstractions.Extensions;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ContainerHost;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Providers.Applications;
using CloudShell.Providers.Docker;
using CloudShell.Providers.DockerCompose;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider()
    .UseDocker()
    .AddDockerComposeOrchestrator(activationPolicy: CloudShellExtensionActivationPolicy.Enabled);

cloudShell.Resources(resources =>
{
    resources
        .AddSqlServer("sql-server")
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest");
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
