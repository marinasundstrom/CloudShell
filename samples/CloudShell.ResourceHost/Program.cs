using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Host.Components;
using CloudShell.Host.Hosting;
using CloudShell.ResourceHost;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder
    .AddCloudShell()
    .AddExtension<SampleResourceExtension>();

cloudShell.Resources(resources =>
{
    var database = resources.Declare(
        SampleResourceProvider.ProviderId,
        "sample:database");

    resources
        .Declare(
            SampleResourceProvider.ProviderId,
            "sample:api")
        .DependsOn(database);
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
