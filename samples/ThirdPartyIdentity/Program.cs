using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.Providers.Configuration;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShellControlPlane();
builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddConfigurationProvider();

cloudShell.Resources(resources =>
{
    resources
        .AddConfigurationStore(
            "configuration:third-party-identity",
            "Third-party Identity Settings")
        .WithEntries(
        [
            new("Authority", builder.Configuration["Authentication:OpenIdConnect:Authority"] ?? string.Empty),
            new("RoleClaimType", builder.Configuration["Authentication:RoleClaimType"] ?? string.Empty)
        ]);
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellAsync();
app.MapCloudShellControlPlane();
app.MapCloudShell<App>();

app.Run();
