using System.Text.Json;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace CloudShell.ControlPlane.Client.Tests;

public sealed class RemoteControlPlaneContractTests
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task RemoteControlPlane_ListsSeededResourcesAndGroups()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        var resources = await controlPlane.ListResourcesAsync();
        var groups = await controlPlane.ListResourceGroupsAsync();

        var network = Assert.Single(resources);
        Assert.Equal("network:contract", network.Id);
        Assert.Equal("Contract Network", network.Name);
        Assert.Equal(PlatformResourceProvider.NetworkResourceType, network.EffectiveTypeId);

        var remoteGroup = Assert.Single(groups);
        Assert.Equal(group.Id, remoteGroup.Id);
        Assert.Equal("Contract Group", remoteGroup.Name);
        Assert.Contains("network:contract", remoteGroup.ResourceIds);

        var resourceGroup = await controlPlane.GetResourceGroupForResourceAsync(network.Id);
        Assert.NotNull(resourceGroup);
        Assert.Equal(remoteGroup.Id, resourceGroup.Id);
    }

    [Fact]
    public async Task RemoteControlPlane_CreatesAndQueriesPlatformResources()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        await controlPlane.CreateResourceAsync(
            new CreateResourceCommand(
                PlatformResourceProvider.ProviderId,
                PlatformResourceProvider.ServiceResourceType,
                "service:contract",
                "Contract Service",
                JsonSerializer.SerializeToElement(
                    new ServiceResourceDefinition(
                        "service:contract",
                        "Contract Service",
                        [new ServiceTarget("network:contract")],
                        [new ServicePort("http", 8080, 5080, "http")],
                        ["network:contract"]),
                    SerializerOptions),
                group.Id));

        var services = await controlPlane.ListResourcesAsync(
            new ResourceQuery(ResourceGroupId: group.Id, ResourceType: PlatformResourceProvider.ServiceResourceType));
        var service = Assert.Single(services);

        Assert.Equal("service:contract", service.Id);
        Assert.Equal("Contract Service", service.Name);
        Assert.Equal(["network:contract"], service.DependsOn);
        Assert.Equal("http://localhost:5080", service.PrimaryEndpoint);

        var registration = await controlPlane.GetResourceRegistrationAsync(service.Id);
        Assert.NotNull(registration);
        Assert.Equal(group.Id, registration.ResourceGroupId);
        Assert.Equal(["network:contract"], registration.DependsOn);
    }

    [Fact]
    public async Task RemoteControlPlane_MapsCapabilitiesAndDeleteResults()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var capabilities = await controlPlane.GetResourceOperationCapabilitiesAsync(["network:contract"]);

        var networkCapabilities = Assert.Single(capabilities);
        Assert.Equal("network:contract", networkCapabilities.Key);
        Assert.True(networkCapabilities.Value.CanManage);
        Assert.True(networkCapabilities.Value.CanDelete);

        var result = await controlPlane.DeleteResourceAsync("network:contract");
        Assert.Contains("removed", result.Message, StringComparison.OrdinalIgnoreCase);

        Assert.Null(await controlPlane.GetResourceAsync("network:contract"));
    }

    [Fact]
    public async Task RemoteControlPlane_MapsLogsAndTraces()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var logs = await controlPlane.ListLogsAsync(new LogQuery(ResourceId: "network:contract"));
        Assert.Empty(logs);

        var span = new TraceSpan(
            "trace-contract",
            "span-contract",
            null,
            "GET /contract",
            "network:contract",
            "contract-service",
            "server",
            "ok",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMilliseconds(42),
            new Dictionary<string, string>
            {
                ["http.method"] = "GET"
            });
        await controlPlane.IngestTraceSpansAsync([span]);

        var spans = await controlPlane.ListTraceSpansAsync(
            new TraceQuery(ResourceId: "network:contract", TraceId: "trace-contract", MaxSpans: 10));

        var remoteSpan = Assert.Single(spans);
        Assert.Equal(span.TraceId, remoteSpan.TraceId);
        Assert.Equal(span.SpanId, remoteSpan.SpanId);
        Assert.Equal(span.ResourceId, remoteSpan.ResourceId);
        Assert.Equal("GET", remoteSpan.SpanAttributes["http.method"]);
    }

    [Fact]
    public async Task RemoteControlPlane_ExportsAndImportsResourceGroupTemplates()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);
        var group = await CreateContractGroupAsync(controlPlane);

        var exported = await controlPlane.ExportResourceGroupTemplateAsync(group.Id);

        Assert.Equal("resourceGroup", exported.Template.Kind);
        Assert.Equal("Contract Group", exported.Template.Name);
        Assert.Empty(exported.Template.Resources);
        Assert.Contains(
            exported.Diagnostics,
            diagnostic => diagnostic.Severity == "Warning" &&
                diagnostic.ResourceName == "Contract Network");

        var imported = await controlPlane.ImportResourceGroupTemplateAsync(
            exported.Template with
            {
                Name = "Imported Contract Group",
                Description = "Imported through the remote control-plane contract."
            });

        Assert.Equal("Imported Contract Group", imported.ResourceGroup.Name);
        Assert.Empty(imported.ImportedResources);
        Assert.Empty(imported.Diagnostics);
    }

    private static RemoteControlPlane CreateClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return new RemoteControlPlane(client);
    }

    private static async Task<WebApplication> CreateAppAsync()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(contentRoot);

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            EnvironmentName = "Development"
        });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Enabled"] = "false",
            ["Persistence:Provider"] = "Sqlite",
            ["Persistence:ConnectionString"] = "Data Source=Data/cloudshell-client-contract.db",
            ["Persistence:IdentityConnectionString"] = "Data Source=Data/identity-client-contract.db"
        });

        var controlPlane = builder.AddCloudShellControlPlane();
        controlPlane.Resources(resources =>
        {
            resources
                .AddNetwork("network:contract", "Contract Network", isDefault: true)
                .Persist();
        });

        var app = builder.Build();
        await app.UseCloudShellControlPlaneAsync();
        app.MapCloudShellControlPlane();
        await app.StartAsync();
        return app;
    }

    private static async Task<ResourceGroup> CreateContractGroupAsync(IResourceManager resources)
    {
        var group = await resources.CreateResourceGroupAsync(
            new CreateResourceGroupCommand(
                "Contract Group",
                "Group used by remote contract tests"));
        await resources.AssignResourceGroupAsync(
            new AssignResourceGroupCommand("network:contract", group.Id));
        return group;
    }
}
