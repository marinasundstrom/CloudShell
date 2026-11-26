using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.Providers.UI;
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

builder.AddCloudShellControlPlane(controlPlane =>
{
    controlPlane
        .AddExtension<SampleResourceControlPlaneExtension>();

    controlPlane.ConfigureInMemoryIdentity(identity =>
    {
        identity.Users.Add(
            "alice",
            password: "CloudShell123!",
            displayName: "Alice Local Developer",
            email: "alice@example.test",
            role: "CloudShell.Reader");
    });

    controlPlane.DefineResources(resources =>
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

        if (builder.Configuration.GetValue<bool>("DesignPreview:Enabled"))
        {
            resources.AddResourceGroup("sample-data", "Data services", "Simulated resources for UI review.");
            resources.AddResourceGroup("sample-messaging", "Messaging", "Simulated resources for UI review.");
            foreach (var id in new[] { "sample:sql", "sample:orders-db", "sample:reporting-db", "sample:archive-db", "sample:analytics-db", "sample:audit-db", "sample:warehouse-db", "sample:unknown-db" })
                resources.Declare(SampleResourceProvider.ProviderId, id).WithResourceGroup("sample-data").WithAutoStart(false);
            foreach (var id in new[] { "sample:broker", "sample:orders-queue", "sample:deadletters" })
                resources.Declare(SampleResourceProvider.ProviderId, id).WithResourceGroup("sample-messaging").WithAutoStart(false);
        }

        api.Allow(alice, CloudShellPermissions.Resources.Read);
        database.Allow(alice, CloudShellPermissions.Resources.Manage);
    });
});

if (builder.Configuration.GetValue<bool>("DesignPreview:Enabled"))
    builder.Services.AddSingleton<IRabbitMQBrokerDashboardProvider, DesignPreviewBrokerDashboardProvider>();

builder.AddCloudShellUi(ui =>
{
    if (builder.Configuration.GetValue<bool>("DesignPreview:Enabled"))
        ui.AddBuiltInProviderResourceManagerUi();

    ui
        .AddExtension<ResourceManagerExtension>()
        .AddExtension<TelemetryExtension>()
        .AddExtension<UsageExtension>()
        .AddExtension<SampleResourceManagerUiExtension>();
});

var app = builder.Build();

await app.UseCloudShellControlPlaneAsync();
await app.UseCloudShellUiAsync();
app.MapCloudShellControlPlane();
app.MapCloudShellUi<App>();

app.Run();
