using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Providers.Applications;

var builder = WebApplication.CreateBuilder(args);

var cloudShell = builder
    .AddCloudShell()
    .AddApplicationProvider();

cloudShell.Resources(resources =>
{
    var api = resources.AddAspNetCoreProject(
        "application:project-reference-api",
        "Project Reference API",
        "samples/CloudShell.ProjectReferenceApi/CloudShell.ProjectReferenceApi.csproj");

    resources
        .AddAspNetCoreProject(
            "application:project-reference-frontend",
            "Project Reference Frontend",
            "samples/CloudShell.ProjectReferenceFrontend/CloudShell.ProjectReferenceFrontend.csproj",
            endpoint: "http://localhost:5218")
        .WithReference(api)
        .DependsOn(api);
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
