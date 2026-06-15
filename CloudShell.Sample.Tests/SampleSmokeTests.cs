using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ContainerHost;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace CloudShell.Sample.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SampleSmokeCollection
{
    public const string Name = "Sample smoke tests";
}

[Collection(SampleSmokeCollection.Name)]
public sealed class SampleSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(45);

    [Fact]
    public async Task ContainerHostSample_DeclaresLocalStorageBackedSqlServerVolume()
    {
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddControlPlane()
            .AddApplicationProvider()
            .Resources(ContainerHostSampleResources.AddResources);

        using var serviceProvider = services.BuildServiceProvider();
        var declarations = serviceProvider
            .GetRequiredService<ResourceDeclarationStore>()
            .GetDeclarations()
            .ToDictionary(declaration => declaration.ResourceId, StringComparer.OrdinalIgnoreCase);
        var platformOptions = serviceProvider.GetRequiredService<PlatformResourceOptions>();
        var platformStore = new PlatformResourceStore(
            platformOptions,
            serviceProvider.GetRequiredService<IHostEnvironment>());
        var platformProvider = new PlatformResourceProvider(platformStore, platformOptions);
        var platformResources = platformProvider
            .GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var applicationProvider = ActivatorUtilities.CreateInstance<ApplicationResourceProvider>(serviceProvider);
        var sqlServer = Assert.Single(applicationProvider.GetResources(), resource =>
            resource.Id == "application:sql-server");
        var descriptor = await applicationProvider.DescribeAsync(
            sqlServer,
            new ResourceOrchestrationDescriptorContext(null, null, null!));
        var workload = descriptor.Configuration.Deserialize<ResourceWorkloadConfiguration>(
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.True(declarations.ContainsKey("storage:local"));
        Assert.True(declarations.ContainsKey("volume:sql-data"));
        Assert.True(declarations.ContainsKey("application:sql-server"));
        Assert.Equal("storage:local", declarations["volume:sql-data"].ParentResourceId);
        Assert.Equal(["storage:local"], declarations["volume:sql-data"].DependsOn);
        Assert.Contains("volume:sql-data", declarations["application:sql-server"].DependsOn);

        var storage = platformResources["storage:local"];
        Assert.Equal(ResourceClass.Storage, storage.ResourceClass);
        Assert.Equal(StorageProviderNames.LocalStorage, storage.Kind);
        Assert.Equal(StorageMedia.FileSystem, storage.ResourceAttributes[ResourceAttributeNames.StorageMedium]);
        Assert.Equal("./Data/storage", storage.ResourceAttributes[ResourceAttributeNames.StorageLocation]);
        Assert.Equal("1", storage.ResourceAttributes[ResourceAttributeNames.StorageVolumeCount]);

        var volume = platformResources["volume:sql-data"];
        Assert.Equal(ResourceClass.Storage, volume.ResourceClass);
        Assert.Equal(["storage:local"], volume.DependsOn);
        Assert.Equal("storage:local", volume.ResourceAttributes[ResourceAttributeNames.VolumeStorageResourceId]);
        Assert.Equal("sql-server", volume.ResourceAttributes[ResourceAttributeNames.VolumeSubPath]);
        Assert.Equal(StorageMedia.FileSystem, volume.ResourceAttributes[ResourceAttributeNames.VolumeStorageMedium]);

        Assert.Equal(ApplicationResourceTypes.ContainerApp, sqlServer.EffectiveTypeId);
        Assert.Equal(ResourceClass.Container, sqlServer.ResourceClass);
        Assert.True(sqlServer.HasCapability(ResourceCapabilityIds.StorageVolumeConsumer));
        Assert.Equal("1", sqlServer.ResourceAttributes[ResourceAttributeNames.VolumeMountCount]);

        var mount = Assert.Single(workload?.WorkloadVolumeMounts ?? []);
        Assert.Equal("volume:sql-data", mount.VolumeReference);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);
        Assert.Equal("data", mount.Name);
        Assert.False(mount.ReadOnly);
        Assert.Equal(StorageVolumeResourceOperationPermissions.MountWrite, mount.RequiredPermission);
    }

    [Fact]
    public async Task ProjectReferenceHost_RendersResourcesAndServesControlPlaneApi()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Project Reference API", resourcesHtml);
        Assert.Contains("Project Reference Frontend", resourcesHtml);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var resources = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-api");
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-frontend");

        var relatedLogsHtml = await host.GetStringAsync(
            "/logs?resourceId=application%3Aproject-reference-frontend&traceId=4bf92f3577b34da6a3ce929d0e0e4736");
        Assert.Contains("Project Reference Frontend", relatedLogsHtml);
        Assert.Contains("Console logs", relatedLogsHtml);
        Assert.Contains("Showing entries correlated with trace", relatedLogsHtml);
        Assert.Contains("Clear trace filter", relatedLogsHtml);

        var relatedActivityHtml = await host.GetStringAsync(
            "/resources/application%3Aproject-reference-frontend/details?tab=activity&traceId=4bf92f3577b34da6a3ce929d0e0e4736");
        Assert.Contains("Activity", relatedActivityHtml);
        Assert.Contains("Showing activity correlated with trace", relatedActivityHtml);
        Assert.Contains("Clear", relatedActivityHtml);
    }

    [Fact]
    public async Task ProjectReferenceHost_HonorsResourceManagerReadOnlySetting()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync(),
            [("ResourceManager__ReadOnly", "true")]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Resource Manager is in read-only mode", resourcesHtml);
        Assert.DoesNotContain(">Add resource<", resourcesHtml);
        Assert.DoesNotContain(">Create group<", resourcesHtml);

        var addResourceHtml = await host.GetStringAsync("/resources/add");
        Assert.Contains("Resource registration is disabled", addResourceHtml);
        Assert.DoesNotContain("Create a resource group", addResourceHtml);
    }

    [Fact]
    public async Task ApplicationTopologyHost_ProjectsSqlStorageAndServiceDiscoveryTopology()
    {
        var frontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var storage = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "storage:application-topology-local");
        var volume = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "volume:application-topology-sql-data");
        var sqlServer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-sql-server");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-api");
        var frontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-frontend");

        var storageAttributes = storage.GetProperty("attributes");
        var volumeAttributes = volume.GetProperty("attributes");
        var sqlAttributes = sqlServer.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");
        var frontendAttributes = frontend.GetProperty("attributes");

        Assert.Equal((int)ResourceState.Stopped, sqlServer.GetProperty("state").GetInt32());
        Assert.Equal((int)ResourceState.Stopped, api.GetProperty("state").GetInt32());
        Assert.Equal((int)ResourceState.Stopped, frontend.GetProperty("state").GetInt32());

        Assert.Equal("cloudshell.storage", storage.GetProperty("typeId").GetString());
        Assert.Equal(StorageProviderNames.LocalStorage, storage.GetProperty("kind").GetString());
        Assert.Equal(StorageMedia.FileSystem, storageAttributes.GetProperty(ResourceAttributeNames.StorageMedium).GetString());
        Assert.Equal("./Data/storage", storageAttributes.GetProperty(ResourceAttributeNames.StorageLocation).GetString());
        Assert.Equal("1", storageAttributes.GetProperty(ResourceAttributeNames.StorageVolumeCount).GetString());

        Assert.Equal("cloudshell.volume", volume.GetProperty("typeId").GetString());
        Assert.Equal("storage:application-topology-local", volume.GetProperty("parentResourceId").GetString());
        Assert.Equal("storage:application-topology-local", volumeAttributes.GetProperty(ResourceAttributeNames.VolumeStorageResourceId).GetString());
        Assert.Equal("sql-server", volumeAttributes.GetProperty(ResourceAttributeNames.VolumeSubPath).GetString());
        Assert.Equal(StorageMedia.FileSystem, volumeAttributes.GetProperty(ResourceAttributeNames.VolumeStorageMedium).GetString());

        Assert.Equal(ApplicationResourceTypes.ContainerApp, sqlServer.GetProperty("typeId").GetString());
        Assert.Equal($"tcp://localhost:{sqlPort}", GetEndpointAddress(sqlServer, "tds"));
        Assert.Equal("1", sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountCount).GetString());
        Assert.Equal("mcr.microsoft.com/mssql/server:2022-latest", sqlAttributes.GetProperty(ResourceAttributeNames.ContainerImage).GetString());
        Assert.Contains(
            "volume:application-topology-sql-data",
            sqlServer.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, api.GetProperty("typeId").GetString());
        Assert.Equal("../Api/CloudShell.ApplicationTopologyApi.csproj", apiAttributes.GetProperty(ResourceAttributeNames.ProjectPath).GetString());
        Assert.Contains(
            "application:application-topology-sql-server",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, frontend.GetProperty("typeId").GetString());
        Assert.Equal($"http://localhost:{frontendPort}", GetEndpointAddress(frontend, "http"));
        Assert.Equal("../Frontend/CloudShell.ApplicationTopologyFrontend.csproj", frontendAttributes.GetProperty(ResourceAttributeNames.ProjectPath).GetString());
        Assert.Contains(
            "application:application-topology-api",
            frontend.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        var sqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details");
        AssertResourceTabsInOrder(
            sqlDetailsHtml,
            ">Overview<",
            ">Runtime<",
            ">Deployment<",
            ">Replicas<",
            ">Configuration<",
            ">Storage<",
            ">Management<",
            ">Activity<");
    }

    [Fact]
    public async Task SettingsAndSecretsSample_ProjectsReferenceBackedEnvironmentResources()
    {
        var apiPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:sample-app");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:sample-app");
        using var host = await SampleProcess.StartAsync(
            "samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj",
            await GetFreePortAsync(),
            [
                ("Samples__SettingsAndSecrets__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("Samples__SettingsAndSecrets__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("Samples__SettingsAndSecrets__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var resources = document.RootElement.EnumerateArray().ToArray();
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:sample-app");
        var secrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:sample-app");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:settings-secrets-api");
        var dependsOn = api
            .GetProperty("dependsOn")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        var identity = api.GetProperty("identity");

        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("secrets.vault", secrets.GetProperty("typeId").GetString());
        Assert.Contains("configuration:sample-app", dependsOn);
        Assert.Contains("secrets-vault:sample-app", dependsOn);
        Assert.Equal("identity:development", identity.GetProperty("providerId").GetString());
        Assert.Equal("settings-secrets-api", identity.GetProperty("name").GetString());

        var grantsJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants?identityResourceId=application%3Asettings-secrets-api&identityName=settings-secrets-api");
        using var grantsDocument = JsonDocument.Parse(grantsJson);
        var grants = grantsDocument.RootElement.EnumerateArray().ToArray();
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "secrets-vault:sample-app" &&
                grant.GetProperty("permission").GetString() == SecretsVaultResourceOperationPermissions.ReadSecrets);
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "configuration:sample-app" &&
                grant.GetProperty("permission").GetString() == ConfigurationStoreResourceOperationPermissions.ReadEntries);

        var provisioning = await host.GetStringAsync(
            "/api/control-plane/v1/resources/application%3Asettings-secrets-api/identity/provisioning-status");
        using var provisioningDocument = JsonDocument.Parse(provisioning);
        Assert.Equal(
            "identity:development",
            provisioningDocument.RootElement.GetProperty("providerId").GetString());
        var provisioningStatus = Assert.Single(provisioningDocument.RootElement.GetProperty("statuses").EnumerateArray());
        var state = provisioningStatus.GetProperty("state");
        if (state.ValueKind == JsonValueKind.String)
        {
            Assert.Equal("provisioned", state.GetString()?.ToLowerInvariant());
        }
        else
        {
            Assert.Equal((int)ResourceIdentityProvisioningState.Provisioned, state.GetInt32());
        }

        var credentialSampleOutput = await RunResourceIdentityCredentialSampleAsync(host);
        Assert.Contains(
            "CloudShell resource credential acquired a token.",
            credentialSampleOutput);
        Assert.Contains(
            "CloudShell Control Plane client listed",
            credentialSampleOutput);

        var apiDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:settings-secrets-api")}/details");
        AssertResourceTabsInOrder(
            apiDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Environment<",
            ">Identity<",
            ">Activity<");

        await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Asettings-secrets-api/actions/start?startDependencies=true");

        var startedApiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var startedDocument = JsonDocument.Parse(startedApiJson);
        var startedResources = startedDocument.RootElement.EnumerateArray().ToArray();
        settings = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "configuration:sample-app");
        secrets = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:sample-app");
        api = Assert.Single(startedResources, resource =>
            resource.GetProperty("id").GetString() == "application:settings-secrets-api");

        var resourceToken = await host.GetClientCredentialsTokenAsync(
            "application:settings-secrets-api/settings-secrets-api",
            "local-development-settings-secrets-api-secret",
            "ControlPlane.Access");
        var apiEndpoint = GetPrimaryEndpointAddress(api);
        var settingsEndpoint = GetEndpointAddress(settings, "entries");
        var secretsEndpoint = GetEndpointAddress(secrets, "secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{apiEndpoint.TrimEnd('/')}/configuration",
            null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(settingsEndpoint, resourceToken, StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{secretsEndpoint.TrimEnd('/')}/sample-api-key",
            resourceToken,
            StartupTimeout);

        var settingsJson = await host.GetAbsoluteStringAsync(settingsEndpoint, resourceToken);
        using var settingsDocument = JsonDocument.Parse(settingsJson);
        Assert.Contains(
            settingsDocument.RootElement.EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");

        var secretJson = await host.GetAbsoluteStringAsync(
            $"{secretsEndpoint.TrimEnd('/')}/sample-api-key",
            resourceToken);
        using var secretDocument = JsonDocument.Parse(secretJson);
        Assert.Equal(
            "local-development-api-key",
            secretDocument.RootElement.GetProperty("value").GetString());

        var apiConfigurationJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/configuration");
        using var apiConfigurationDocument = JsonDocument.Parse(apiConfigurationJson);
        Assert.Equal(
            "connected",
            apiConfigurationDocument.RootElement.GetProperty("status").GetString());
        var apiEntries = apiConfigurationDocument.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(
            apiEntries,
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");

        var serviceDiscoveryJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/service-discovery/configuration");
        using var serviceDiscoveryDocument = JsonDocument.Parse(serviceDiscoveryJson);
        Assert.Equal(
            "connected",
            serviceDiscoveryDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "https+http://sample-app-settings",
            serviceDiscoveryDocument.RootElement.GetProperty("source").GetString());
        var serviceDiscoveryEntries = serviceDiscoveryDocument.RootElement
            .GetProperty("entries")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(
            serviceDiscoveryEntries,
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");

        var apiSecretJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/secrets/sample-api-key");
        using var apiSecretDocument = JsonDocument.Parse(apiSecretJson);
        Assert.Equal(
            "connected",
            apiSecretDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "local-development-api-key",
            apiSecretDocument.RootElement.GetProperty("value").GetString());
    }

    [Fact]
    public async Task SplitHostingSample_RendersUiThroughRemoteControlPlane()
    {
        var controlPlanePort = await GetFreePortAsync();
        var uiPort = await GetFreePortAsync();

        using var controlPlane = await SampleProcess.StartAsync(
            "samples/SplitHosting/ControlPlane/CloudShell.SplitHosting.ControlPlane.csproj",
            controlPlanePort,
            environment:
            [
                ("Authentication__BuiltInAuthority__Issuer", $"http://localhost:{controlPlanePort}")
            ]);
        await controlPlane.WaitForHttpOkAsync("/openapi/control-plane-v1.json", StartupTimeout);

        using var ui = await SampleProcess.StartAsync(
            "samples/SplitHosting/UI/CloudShell.SplitHosting.UI.csproj",
            uiPort,
            environment:
            [
                ("CloudShell__ControlPlane__BaseAddress", controlPlane.BaseAddress.ToString())
            ]);
        await ui.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await ui.GetStringAsync("/resources");
        Assert.Contains("Split Sample Network", resourcesHtml);

        var token = await controlPlane.GetClientCredentialsTokenAsync(
            "cloudshell-split-ui",
            "local-development-client-secret",
            "ControlPlane.Access");
        var apiJson = await controlPlane.GetStringAsync(
            "/api/control-plane/v1/resources",
            token);
        using var document = JsonDocument.Parse(apiJson);
        var resource = Assert.Single(document.RootElement.EnumerateArray());
        Assert.Equal("network:split-sample", resource.GetProperty("id").GetString());
    }

    [Fact]
    public async Task ResourceHostSample_ExecutesResourceActionFromAdvertisedHref()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/CloudShell.ResourceHost/CloudShell.ResourceHost.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var apiResource = document.RootElement.EnumerateArray().Single(resource =>
            resource.GetProperty("id").GetString() == "sample:api");
        Assert.Equal((int)ResourceState.Running, apiResource.GetProperty("state").GetInt32());

        var stopAction = apiResource
            .GetProperty("resourceActions")
            .GetProperty("stop");
        Assert.Equal("POST", stopAction.GetProperty("method").GetString());
        var stopHref = stopAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The stop action did not include an href.");

        var actionJson = await host.SendAsync(HttpMethod.Post, stopHref);
        using var actionDocument = JsonDocument.Parse(actionJson);
        Assert.Contains(
            "Stop completed",
            actionDocument.RootElement.GetProperty("message").GetString());

        var stoppedJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("sample:api")}");
        using var stoppedDocument = JsonDocument.Parse(stoppedJson);
        var stoppedResource = stoppedDocument.RootElement;
        Assert.Equal((int)ResourceState.Stopped, stoppedResource.GetProperty("state").GetInt32());
        Assert.True(stoppedResource.GetProperty("resourceActions").TryGetProperty("start", out _));

        var activityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("sample:api")}/details?tab=activity");
        Assert.Contains("Activity", activityHtml);
        Assert.Contains("Event type", activityHtml);
        Assert.Contains("Triggered by", activityHtml);
        Assert.Contains("Time range", activityHtml);
        Assert.Contains("Lifecycle actions", activityHtml);
        Assert.Contains("Lifecycle events", activityHtml);
        Assert.Contains("action.lifecycle.stop", activityHtml);
        Assert.Contains("event.lifecycle.stopped", activityHtml);
        Assert.Contains("Stop completed", activityHtml);
    }

    [Fact]
    public async Task ContainerAppDeploymentSample_UpdatesMockImageTagThroughRevisionApi()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:sample-api");
        var registry = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:container:sample-registry");

        Assert.Equal("localhost:5023", app.GetProperty("attributes").GetProperty("container.registry").GetString());
        Assert.Equal("localhost:5023", registry.GetProperty("attributes").GetProperty("container.registry").GetString());

        var updateJson = await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/container-apps/v1/application%3Asample-api/revisions",
            """
            {
              "image": "cloudshell/mock-api:20260608.2",
              "restartIfRunning": false,
              "triggeredBy": "sample-smoke-test"
            }
            """);
        using var updateDocument = JsonDocument.Parse(updateJson);

        Assert.Contains(
            "cloudshell/mock-api:20260608.2",
            updateDocument.RootElement.GetProperty("message").GetString());

        var updatedJson = await host.GetStringAsync(
            "/api/control-plane/v1/resources/application%3Asample-api");
        using var updatedDocument = JsonDocument.Parse(updatedJson);
        var updatedAttributes = updatedDocument.RootElement.GetProperty("attributes");
        Assert.Equal(
            "cloudshell/mock-api:20260608.2",
            updatedAttributes.GetProperty("container.image").GetString());
        Assert.NotEqual(
            "unrevisioned",
            updatedAttributes.GetProperty("container.revision").GetString());
    }

    [Fact]
    public async Task HostVirtualNetworkSample_ProjectsVirtualNetworkAndHostProvider()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var network = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "network:sample-vnet");
        var attributes = network.GetProperty("attributes");

        Assert.Equal("cloudshell.virtualNetwork", network.GetProperty("typeId").GetString());
        Assert.Equal("providerRequired", attributes.GetProperty("network.hostReadiness").GetString());
        Assert.Equal("networking:host-local", attributes.GetProperty("network.mappingProviders").GetString());

        var endpoint = Assert.Single(network.GetProperty("endpoints").EnumerateArray());
        Assert.Equal("api-public", endpoint.GetProperty("name").GetString());
        Assert.Equal("http://localhost:5290", endpoint.GetProperty("address").GetString());
        Assert.True(endpoint.GetProperty("isExternal").GetBoolean());

        var mapping = Assert.Single(network.GetProperty("endpointMappings").EnumerateArray());
        Assert.Equal("mapping:api-public", mapping.GetProperty("id").GetString());
        Assert.Equal("network:sample-vnet", mapping.GetProperty("source").GetProperty("resourceId").GetString());
        Assert.Equal("api-public", mapping.GetProperty("source").GetProperty("endpointName").GetString());
        Assert.Equal("application:vnet-api", mapping.GetProperty("target").GetProperty("resourceId").GetString());
        Assert.Equal("http", mapping.GetProperty("target").GetProperty("endpointName").GetString());
        Assert.Equal("networking:host-local", mapping.GetProperty("providerResourceId").GetString());

        var reconcileAction = network
            .GetProperty("resourceActions")
            .GetProperty("reconcileEndpointMappings");
        Assert.Equal("Reconcile endpoint mappings", reconcileAction.GetProperty("displayName").GetString());

        var capabilitiesJson = await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/capabilities",
            """
            {
              "resourceIds": [
                "network:sample-vnet"
              ]
            }
            """);
        using var capabilitiesDocument = JsonDocument.Parse(capabilitiesJson);
        var networkCapabilities = Assert.Single(capabilitiesDocument.RootElement.EnumerateArray());
        var reconcileCapability = Assert.Single(
            networkCapabilities.GetProperty("resourceActionCapabilities").EnumerateArray(),
            capability => capability.GetProperty("actionId").GetString() == "reconcileEndpointMappings");

        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "networking:host-local");
        Assert.True(reconcileCapability.GetProperty("canExecute").GetBoolean());
        Assert.Equal(JsonValueKind.Null, reconcileCapability.GetProperty("reason").ValueKind);
    }

    [Fact]
    public async Task LoadBalancerSample_AppliesTraefikConfigurationFromAdvertisedAction()
    {
        var root = SampleProcess.FindRepositoryRoot();
        var dataDirectory = Path.Combine(root, "samples", "LoadBalancer", "Data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        using var host = await SampleProcess.StartAsync(
            "samples/LoadBalancer/CloudShell.LoadBalancer.csproj",
            await GetFreePortAsync(),
            [("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME", "true")]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var loadBalancer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "load-balancer:public");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        var postgres = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:postgres");
        var dnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local");
        var appName = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local:name:app-cloudshell-local");
        var apiName = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local:name:api-cloudshell-local");
        var attributes = loadBalancer.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");
        var postgresAttributes = postgres.GetProperty("attributes");
        var dnsAttributes = dnsZone.GetProperty("attributes");
        var appNameAttributes = appName.GetProperty("attributes");
        var apiNameAttributes = apiName.GetProperty("attributes");

        Assert.Equal("cloudshell.loadBalancer", loadBalancer.GetProperty("typeId").GetString());
        Assert.Equal("traefik", attributes.GetProperty("loadBalancer.provider").GetString());
        Assert.Equal("docker:sample-host", attributes.GetProperty("loadBalancer.hostResourceId").GetString());
        Assert.Equal("3", attributes.GetProperty("loadBalancer.routes").GetString());
        Assert.Equal(3, loadBalancer.GetProperty("loadBalancerRoutes").GetArrayLength());
        Assert.Equal("traefik/whoami:v1.10", apiAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", apiAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("postgres:16-alpine", postgresAttributes.GetProperty("container.image").GetString());
        Assert.Equal("cloudshell.dnsZone", dnsZone.GetProperty("typeId").GetString());
        Assert.Equal("2", dnsAttributes.GetProperty("dns.records").GetString());
        Assert.Equal("cloudshell.nameMapping", appName.GetProperty("typeId").GetString());
        Assert.Equal("app.cloudshell.local", appNameAttributes.GetProperty("nameMapping.hostName").GetString());
        Assert.Equal("load-balancer:public", appNameAttributes.GetProperty("nameMapping.targetResourceId").GetString());
        Assert.Equal("http", appNameAttributes.GetProperty("nameMapping.targetEndpointName").GetString());
        Assert.Equal("ProviderSelected", appNameAttributes.GetProperty("nameMapping.materializationStatus").GetString());
        Assert.Equal("api.cloudshell.local", apiNameAttributes.GetProperty("nameMapping.hostName").GetString());
        Assert.Equal("load-balancer:public", apiNameAttributes.GetProperty("nameMapping.targetResourceId").GetString());
        Assert.Equal("ProviderSelected", apiNameAttributes.GetProperty("nameMapping.materializationStatus").GetString());

        var applyAction = loadBalancer
            .GetProperty("resourceActions")
            .GetProperty("applyLoadBalancerConfiguration");
        var applyHref = applyAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The load balancer apply action did not include an href.");

        var applyJson = await host.SendAsync(HttpMethod.Post, applyHref);
        using var applyDocument = JsonDocument.Parse(applyJson);
        Assert.Contains(
            "Applied Traefik configuration for 3 route(s)",
            applyDocument.RootElement.GetProperty("message").GetString());

        var configPath = Path.Combine(dataDirectory, "traefik", "load-balancer-public.dynamic.yml");
        var config = await File.ReadAllTextAsync(configPath);
        Assert.Contains("Host(`app.cloudshell.local`)", config);
        Assert.Contains("Host(`api.cloudshell.local`) && PathPrefix(`/v1`)", config);
        Assert.Contains("url: \"http://cloudshell-application-web:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-1:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-2:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-api-replica-3:80\"", config);
        Assert.Contains("HostSNI(`*`)", config);
        Assert.Contains("address: \"cloudshell-application-postgres:5432\"", config);
    }

    private static async Task<int> GetFreePortAsync()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
            await Task.Yield();
        }
    }

    private static async Task<int> GetServiceBasePortAsync(string resourceId)
    {
        var offset = GetStableServicePortOffset(resourceId);
        while (true)
        {
            var port = await GetFreePortAsync();
            var basePort = port - offset;
            if (basePort > 1024)
            {
                return basePort;
            }
        }
    }

    private static int GetStableServicePortOffset(string resourceId)
    {
        uint hash = 0;
        foreach (var character in resourceId)
        {
            hash = unchecked((hash * 31) + char.ToUpperInvariant(character));
        }

        return (int)(hash % 1000);
    }

    private static string GetEndpointAddress(JsonElement resource, string endpointName)
    {
        var endpoint = resource
            .GetProperty("endpoints")
            .EnumerateArray()
            .Single(endpoint =>
                endpoint.GetProperty("name").GetString() == endpointName);
        return endpoint.GetProperty("address").GetString() ??
            throw new InvalidOperationException($"Endpoint '{endpointName}' did not include an address.");
    }

    private static string GetPrimaryEndpointAddress(JsonElement resource) =>
        resource.GetProperty("primaryEndpoint").GetString() ??
        throw new InvalidOperationException("The resource did not include a primary endpoint.");

    private static async Task<string> RunResourceIdentityCredentialSampleAsync(
        SampleProcess host)
    {
        var root = SampleProcess.FindRepositoryRoot();
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--no-build");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(Path.Combine(
            root,
            "samples/ResourceIdentityCredential/CloudShell.ResourceIdentityCredential.csproj"));
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add("ControlPlane.Access");
        startInfo.Environment["CLOUDSHELL_IDENTITY_TOKEN_ENDPOINT"] =
            new Uri(host.BaseAddress, "/api/auth/v1/token").ToString();
        startInfo.Environment["CLOUDSHELL_IDENTITY_CLIENT_ID"] =
            "application:settings-secrets-api/settings-secrets-api";
        startInfo.Environment["CLOUDSHELL_IDENTITY_CLIENT_SECRET"] =
            "local-development-settings-secrets-api-secret";
        startInfo.Environment["CLOUDSHELL_IDENTITY_SCOPE"] = "ControlPlane.Access";
        startInfo.Environment["CloudShell__ControlPlane__BaseAddress"] =
            host.BaseAddress.ToString();

        using var process = Process.Start(startInfo) ??
            throw new InvalidOperationException("Could not start resource identity credential sample.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        var output = await outputTask;
        var error = await errorTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Resource identity credential sample exited with code {process.ExitCode}." +
                $"{Environment.NewLine}{output}{Environment.NewLine}{error}");
        }

        return output;
    }

    private static void AssertResourceTabsInOrder(string html, params string[] expected)
    {
        const string tabListMarker = "aria-label=\"Resource views\"";
        var tabListStart = html.IndexOf(tabListMarker, StringComparison.Ordinal);
        Assert.True(tabListStart >= 0, "Expected to find the resource tab list.");

        var tabListEnd = html.IndexOf("class=\"panel registration-host\"", tabListStart, StringComparison.Ordinal);
        Assert.True(tabListEnd > tabListStart, "Expected the resource tab list to appear before the detail host.");

        AssertInOrder(html[tabListStart..tabListEnd], expected);
    }

    private static void AssertInOrder(string value, params string[] expected)
    {
        var previousIndex = -1;
        foreach (var item in expected)
        {
            var index = value.IndexOf(item, StringComparison.Ordinal);
            Assert.True(index >= 0, $"Expected to find '{item}'.");
            Assert.True(
                index > previousIndex,
                $"Expected '{item}' to appear after the previous item.");
            previousIndex = index;
        }
    }

    private sealed class TestHostEnvironment(string contentRootPath) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "CloudShell.Sample.Tests";

        public string ContentRootPath { get; set; } = contentRootPath;

        public IFileProvider ContentRootFileProvider { get; set; } =
            new NullFileProvider();
    }

    private sealed class SampleProcess : IDisposable
    {
        private readonly Process process;
        private readonly StringBuilder output = new();

        private SampleProcess(Process process, Uri baseAddress)
        {
            this.process = process;
            BaseAddress = baseAddress;
        }

        public Uri BaseAddress { get; }

        public static Task<SampleProcess> StartAsync(
            string projectPath,
            int port,
            IReadOnlyList<(string Key, string Value)>? environment = null)
        {
            var root = FindRepositoryRoot();
            var projectFile = Path.Combine(root, projectPath);
            var projectDirectory = Path.GetDirectoryName(projectFile) ??
                throw new InvalidOperationException($"Could not resolve sample project directory for '{projectPath}'.");
            var dataDirectory = Path.Combine(projectDirectory, "Data");
            if (Directory.Exists(dataDirectory))
            {
                Directory.Delete(dataDirectory, recursive: true);
            }

            var baseAddress = new Uri($"http://127.0.0.1:{port}");
            var startInfo = new ProcessStartInfo("dotnet")
            {
                WorkingDirectory = root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            startInfo.ArgumentList.Add("run");
            startInfo.ArgumentList.Add("--no-build");
            startInfo.ArgumentList.Add("--project");
            startInfo.ArgumentList.Add(projectFile);
            startInfo.ArgumentList.Add("--");
            startInfo.ArgumentList.Add("--urls");
            startInfo.ArgumentList.Add(baseAddress.ToString());
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Could not start sample project '{projectPath}'.");
            var sample = new SampleProcess(process, baseAddress);
            sample.Capture(process.StandardOutput);
            sample.Capture(process.StandardError);
            return Task.FromResult(sample);
        }

        public async Task WaitForHttpOkAsync(string path, TimeSpan timeout)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(3)
            };
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            Exception? lastException = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample process exited with code {process.ExitCode} before '{path}' was ready.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    using var response = await client.GetAsync(path);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample process did not return a successful response for '{path}' within {timeout}." +
                $"{Environment.NewLine}{lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        public async Task<string> GetStringAsync(string path, string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = StartupTimeout
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task WaitForAbsoluteHttpOkAsync(
            string url,
            string? bearerToken,
            TimeSpan timeout)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            Exception? lastException = null;
            string? lastStatus = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample process exited with code {process.ExitCode} before '{url}' was ready.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Get, url);
                    if (!string.IsNullOrWhiteSpace(bearerToken))
                    {
                        request.Headers.Authorization = new("Bearer", bearerToken);
                    }

                    using var response = await client.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        return;
                    }

                    lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: " +
                        await response.Content.ReadAsStringAsync();
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample process did not return a successful response for '{url}' within {timeout}." +
                $"{Environment.NewLine}{lastStatus ?? lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        public async Task<string> GetAbsoluteStringAsync(string url, string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> SendAsync(
            HttpMethod method,
            string path,
            string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = StartupTimeout
            };
            using var request = new HttpRequestMessage(method, path);
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> SendJsonAsync(
            HttpMethod method,
            string path,
            string json,
            string? bearerToken = null)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var request = new HttpRequestMessage(method, path)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            };
            if (!string.IsNullOrWhiteSpace(bearerToken))
            {
                request.Headers.Authorization = new("Bearer", bearerToken);
            }

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetClientCredentialsTokenAsync(
            string clientId,
            string clientSecret,
            string scope)
        {
            using var client = new HttpClient
            {
                BaseAddress = BaseAddress,
                Timeout = TimeSpan.FromSeconds(10)
            };
            using var response = await client.PostAsync(
                "/api/auth/v1/token",
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "client_credentials",
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["scope"] = scope
                }));
            response.EnsureSuccessStatusCode();

            using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return document.RootElement.GetProperty("access_token").GetString() ??
                throw new InvalidOperationException("The token endpoint returned no access token.");
        }

        public void Dispose()
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            process.Dispose();
        }

        private void Capture(StreamReader reader)
        {
            _ = Task.Run(async () =>
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (output)
                    {
                        output.AppendLine(line);
                    }
                }
            });
        }

        private string GetOutput()
        {
            lock (output)
            {
                return output.ToString();
            }
        }

        public static string FindRepositoryRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "CloudShell.sln")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
