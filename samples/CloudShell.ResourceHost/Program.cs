using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.Hosting;
using CloudShell.Hosting.Components;
using CloudShell.Hosting.ResourceManager;
using CloudShell.Hosting.Shell;
using CloudShell.ControlPlane.Authentication;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.ResourceHost;

var builder = CloudShellApplication.CreateBuilder(args);

var cloudShell = builder.AddCloudShell();

cloudShell
    .AddExtension<ResourceManagerExtension>()
    .AddExtension<ObservabilityExtension>()
    .AddExtension<SampleResourceExtension>();

cloudShell.ConfigureInMemoryIdentity(identity =>
{
    identity.Users.Add(
        "alice",
        password: "CloudShell123!",
        displayName: "Alice Local Developer",
        email: "alice@example.test",
        role: "CloudShell.Reader");
});

cloudShell.DefineResources(resources =>
{
    var alice = resources.GetIdentityProvider().GetUser("alice");
    var database = resources.Declare(
        SampleResourceProvider.ProviderId,
        "sample:database");
    var api = resources
        .Declare(
            SampleResourceProvider.ProviderId,
            "sample:api")
        .DependsOn(database);
    resources.Declare(
        SampleResourceProvider.ProviderId,
        "sample:worker");

    api.Allow(alice, CloudShellPermissions.Resources.Read);
    database.Allow(alice, CloudShellPermissions.Resources.Manage);
});

var app = builder.Build();

await app.UseCloudShellAsync();
app.MapCloudShell<App>();

app.Run();
