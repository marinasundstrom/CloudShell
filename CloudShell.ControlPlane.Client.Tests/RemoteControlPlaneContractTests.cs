using System.Text.Json;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Shell;
using CloudShell.ControlPlane.Hosting;
using CloudShell.ControlPlane.ResourceManager;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;

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
        Assert.Equal(ResourceClass.Network, network.ResourceClass);
        Assert.Equal("Default", network.ResourceAttributes[ResourceAttributeNames.NetworkKind]);

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
            new ResourceQuery(
                ResourceGroupId: group.Id,
                ResourceType: PlatformResourceProvider.ServiceResourceType,
                ResourceClass: ResourceClass.Service));
        var service = Assert.Single(services);

        var networkResources = await controlPlane.ListResourcesAsync(
            new ResourceQuery(ResourceClass: ResourceClass.Network));
        Assert.Single(networkResources);

        Assert.Equal("service:contract", service.Id);
        Assert.Equal("Contract Service", service.Name);
        Assert.Equal(["network:contract"], service.DependsOn);
        Assert.Equal("http://localhost:5080", service.PrimaryEndpoint);
        Assert.Equal(ResourceClass.Service, service.ResourceClass);
        Assert.Equal("1", service.ResourceAttributes[ResourceAttributeNames.ServiceTargetCount]);
        Assert.Equal("1", service.ResourceAttributes[ResourceAttributeNames.ServicePortCount]);

        var registration = await controlPlane.GetResourceRegistrationAsync(service.Id);
        Assert.NotNull(registration);
        Assert.Equal(group.Id, registration.ResourceGroupId);
        Assert.Equal(["network:contract"], registration.DependsOn);
    }

    [Fact]
    public async Task ControlPlaneApi_FiltersResourcesByClass()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/control-plane/v1/resources?resourceClass=Network");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("network:contract", resource.GetProperty("id").GetString());
        Assert.Equal((int)ResourceClass.Network, resource.GetProperty("resourceClass").GetInt32());
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
    public async Task ControlPlaneApi_ExposesResourceActionsAsHypermediaAffordances()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/api/control-plane/v1/resources");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var resource = document.RootElement
            .EnumerateArray()
            .Single(item => item.GetProperty("id").GetString() == ContractLifecycleResourceProvider.ResourceId);
        var actions = resource.GetProperty("resourceActions");
        var stop = actions.GetProperty(ResourceActionIds.Stop);

        Assert.False(resource.TryGetProperty("actions", out _));
        Assert.Equal((int)ResourceClass.Executable, resource.GetProperty("resourceClass").GetInt32());
        Assert.Equal(JsonValueKind.Object, resource.GetProperty("attributes").ValueKind);
        Assert.Equal(JsonValueKind.Object, actions.ValueKind);
        Assert.Equal(ResourceActionIds.Stop, stop.GetProperty("id").GetString());
        Assert.Equal("Stop", stop.GetProperty("displayName").GetString());
        Assert.Equal("POST", stop.GetProperty("method").GetString());
        Assert.Equal(
            "/api/control-plane/v1/resources/contract%3Alifecycle/actions/stop",
            stop.GetProperty("href").GetString());
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
    public async Task RemoteSettingsProvider_ManagesEnvironmentSettingsWhenAuthenticationIsDisabled()
    {
        await using var app = await CreateAppAsync();
        var settingsProvider = CreateSettingsClient(app);

        await settingsProvider.SetSettingAsync(CloudShellUserSettingKeys.ThemeMode, "Dark");

        var setting = await settingsProvider.GetSettingAsync(CloudShellUserSettingKeys.ThemeMode);
        var settings = await settingsProvider.GetSettingsAsync();

        Assert.Equal("Dark", setting?.Value);
        Assert.Equal("Dark", settings[CloudShellUserSettingKeys.ThemeMode].Value);

        await settingsProvider.RemoveSettingAsync(CloudShellUserSettingKeys.ThemeMode);

        Assert.Null(await settingsProvider.GetSettingAsync(CloudShellUserSettingKeys.ThemeMode));
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

        Assert.NotNull(imported.ResourceGroup);
        Assert.Equal("Imported Contract Group", imported.ResourceGroup.Name);
        Assert.Empty(imported.ImportedResources);
        Assert.Empty(imported.Diagnostics);
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCreateResourceGroupRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resource-groups",
            new
            {
                name = " ",
                description = "Invalid group"
            });

        await AssertProblemAsync(response, "Name is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCreateResourceRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceType = PlatformResourceProvider.NetworkResourceType,
                resourceId = "network:invalid",
                name = "Invalid Network",
                configuration = (object?)null
            });

        await AssertProblemAsync(response, "Configuration is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForResourceClassMismatch()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceType = PlatformResourceProvider.ServiceResourceType,
                resourceId = "service:invalid",
                name = "Invalid Service",
                configuration = new ServiceResourceDefinition(
                    "service:invalid",
                    "Invalid Service",
                    [],
                    [],
                    []),
                resourceClass = ResourceClass.Network
            });

        await AssertProblemAsync(
            response,
            "Resource 'service:invalid' uses type 'cloudshell.service' which requires class 'Service', but creation request declares class 'Network'.",
            ControlPlaneErrorCodes.ResourceClassMismatch);
    }

    [Fact]
    public async Task RemoteControlPlane_ThrowsContractErrorForInvalidRequest()
    {
        await using var app = await CreateAppAsync();
        var controlPlane = CreateClient(app);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.CreateResourceGroupAsync(
                new CreateResourceGroupCommand(" ", "Invalid group")));

        Assert.Equal(ControlPlaneErrorCodes.InvalidRequest, exception.Error.Code);
        Assert.Equal("Name is required.", exception.Message);
    }

    [Fact]
    public async Task RemoteControlPlane_ThrowsContractErrorForMissingResourceAction()
    {
        await using var app = await CreateAppAsync(includeLifecycleResource: true);
        var controlPlane = CreateClient(app);

        var exception = await Assert.ThrowsAsync<ControlPlaneException>(() =>
            controlPlane.ExecuteResourceActionAsync(
                ContractLifecycleResourceProvider.ResourceId,
                "missing"));

        Assert.Equal(ControlPlaneErrorCodes.ResourceActionNotFound, exception.Error.Code);
        Assert.Equal(
            "Resource 'contract:lifecycle' does not expose action 'missing'.",
            exception.Message);
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForMissingDeleteResource()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.DeleteAsync("/api/control-plane/v1/resources/missing");

        await AssertProblemAsync(
            response,
            "Resource 'missing' is not registered.",
            ControlPlaneErrorCodes.ResourceNotRegistered,
            HttpStatusCode.NotFound,
            "Resource not found");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidCapabilitiesRequest()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resources/capabilities",
            new
            {
                resourceIds = (string[]?)null
            });

        await AssertProblemAsync(response, "ResourceIds is required.");
    }

    [Fact]
    public async Task ControlPlaneApi_ReturnsProblemForInvalidRegistrationDependencies()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var response = await client.PostAsJsonAsync(
            "/api/control-plane/v1/registrations",
            new
            {
                providerId = PlatformResourceProvider.ProviderId,
                resourceId = "network:contract",
                dependsOn = new[] { " " }
            });

        await AssertProblemAsync(response, "DependsOn cannot contain empty values.");
    }

    private static RemoteControlPlane CreateClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return new RemoteControlPlane(client);
    }

    private static RemoteCloudShellUserSettingsProvider CreateSettingsClient(WebApplication app)
    {
        var client = app.GetTestClient();
        client.BaseAddress = new Uri("http://localhost");
        return new RemoteCloudShellUserSettingsProvider(client);
    }

    private static async Task<WebApplication> CreateAppAsync(
        bool includeLifecycleResource = false)
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
        if (includeLifecycleResource)
        {
            builder.Services.AddSingleton<IResourceProvider, ContractLifecycleResourceProvider>();
        }

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

        if (includeLifecycleResource)
        {
            await using var scope = app.Services.CreateAsyncScope();
            var registrations = scope.ServiceProvider.GetRequiredService<IResourceRegistrationStore>();
            await registrations.RegisterAsync(
                ContractLifecycleResourceProvider.ProviderId,
                ContractLifecycleResourceProvider.ResourceId);
        }

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

    private static async Task AssertProblemAsync(
        HttpResponseMessage response,
        string expectedDetail,
        string expectedCode = ControlPlaneErrorCodes.InvalidRequest,
        HttpStatusCode expectedStatusCode = HttpStatusCode.BadRequest,
        string expectedTitle = "Control plane request failed")
    {
        Assert.Equal(expectedStatusCode, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedTitle, document.RootElement.GetProperty("title").GetString());
        Assert.Equal(expectedDetail, document.RootElement.GetProperty("detail").GetString());
        Assert.Equal(expectedCode, document.RootElement.GetProperty("code").GetString());
    }

    private sealed class ContractLifecycleResourceProvider : IResourceProvider
    {
        public const string ProviderId = "contract.lifecycle";
        public const string ResourceId = "contract:lifecycle";

        public string Id => ProviderId;

        public string DisplayName => "Contract Lifecycle";

        public IReadOnlyList<Resource> GetResources() =>
        [
            new(
                ResourceId,
                "Contract Lifecycle",
                "Lifecycle",
                DisplayName,
                "local",
                ResourceState.Running,
                [],
                "1.0",
                DateTimeOffset.UtcNow,
                [],
                TypeId: "contract.lifecycle",
                ResourceClass: ResourceClass.Executable,
                Actions:
                [
                    ResourceAction.Stop,
                    ResourceAction.Restart
                ])
        ];
    }
}
