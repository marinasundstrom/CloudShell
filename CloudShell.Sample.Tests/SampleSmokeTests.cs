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

        const string traceId = "4bf92f3577b34da6a3ce929d0e0e4736";
        await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/traces/ingest",
            $$"""
            {
              "spans": [
                {
                  "traceId": "{{traceId}}",
                  "spanId": "00f067aa0ba902b7",
                  "parentSpanId": null,
                  "name": "GET /upstream",
                  "resourceId": "application:project-reference-frontend",
                  "serviceName": "project-reference-frontend",
                  "kind": "Server",
                  "status": "Unset",
                  "startTime": "2026-06-16T00:00:00Z",
                  "duration": "00:00:00.1250000",
                  "attributes": {
                    "http.route": "/upstream"
                  }
                }
              ]
            }
            """);

        var traceHtml = await host.GetStringAsync(
            $"/observability/traces?resourceId=application%3Aproject-reference-frontend&traceId={traceId}");
        Assert.Contains("Trace chart", traceHtml);
        Assert.Contains("id=\"trace-source-filter\"", traceHtml);
        Assert.Contains("Related logs", traceHtml);
        Assert.Contains("Related activity", traceHtml);
        Assert.Contains("Open resource", traceHtml);
        Assert.Contains("<fluent-anchor", traceHtml);
        Assert.Contains(
            $"href=\"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&amp;traceId=4bf92f3577b34da6a3ce929d0e0e4736\"",
            traceHtml);
        Assert.Contains(
            $"href=\"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Activity.Value)}&amp;traceId=4bf92f3577b34da6a3ce929d0e0e4736&amp;spanId=00f067aa0ba902b7\"",
            traceHtml);
        Assert.Contains("href=\"/resources/application%3Aproject-reference-frontend/details\"", traceHtml);

        var relatedLogsHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&traceId={traceId}");
        Assert.Contains("Telemetry", relatedLogsHtml);
        Assert.Contains("Resource telemetry", relatedLogsHtml);
        Assert.Contains("Project Reference Frontend", relatedLogsHtml);
        Assert.Contains("Console logs", relatedLogsHtml);
        Assert.Contains("id=\"log-source-filter\"", relatedLogsHtml);
        Assert.Contains("Showing entries correlated with trace", relatedLogsHtml);
        Assert.Contains("Clear trace filter", relatedLogsHtml);

        var relatedTracesHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Traces.Value)}&traceId={traceId}");
        Assert.Contains("Telemetry", relatedTracesHtml);
        Assert.Contains("Resource telemetry", relatedTracesHtml);
        Assert.Contains("Trace chart", relatedTracesHtml);
        Assert.Contains("id=\"trace-source-filter\"", relatedTracesHtml);
        Assert.Contains("Related logs", relatedTracesHtml);
        Assert.Contains("Related activity", relatedTracesHtml);
        Assert.Contains("Clear trace filter", relatedTracesHtml);
        Assert.Contains(
            $"href=\"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&amp;traceId=4bf92f3577b34da6a3ce929d0e0e4736\"",
            relatedTracesHtml);

        var relatedActivityHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Activity.Value)}&traceId={traceId}&spanId=00f067aa0ba902b7");
        Assert.Contains("Activity", relatedActivityHtml);
        Assert.Contains("Showing activity correlated with trace", relatedActivityHtml);
        Assert.Contains("Showing activity correlated with span", relatedActivityHtml);
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
    public async Task ProjectReferenceHost_AddResourceUsesNameWithoutDisplayNameInput()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var addResourceHtml = await host.GetStringAsync(
            "/resources/add?type=application.aspnet-core-project");
        Assert.Contains("Name", addResourceHtml);
        Assert.DoesNotContain("Display name", addResourceHtml);
        Assert.DoesNotContain("web-application-display-name", addResourceHtml);
    }

    [Fact]
    public async Task ApplicationTopologyHost_ProjectsSqlStorageAndServiceDiscoveryTopology()
    {
        var frontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
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
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:application-topology");
        var secrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:application-topology");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-api");
        var frontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-frontend");
        var dnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:application-topology-local");
        var nameMapping = Assert.Single(resources, resource =>
            resource.GetProperty("typeId").GetString() == PlatformResourceProvider.NameMappingResourceType
            && resource.GetProperty("attributes")
                .GetProperty(ResourceAttributeNames.NameMappingHostName)
                .GetString() == "app.application-topology.cloudshell.local");

        var storageAttributes = storage.GetProperty("attributes");
        var volumeAttributes = volume.GetProperty("attributes");
        var sqlAttributes = sqlServer.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");
        var frontendAttributes = frontend.GetProperty("attributes");
        var dnsZoneAttributes = dnsZone.GetProperty("attributes");
        var nameMappingAttributes = nameMapping.GetProperty("attributes");

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
        Assert.Equal("0", sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountMaterializedCount).GetString());
        Assert.Equal(
            ResourceVolumeMountMaterializationStatus.NotActive,
            sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountMaterializationStatus).GetString());
        Assert.Equal("mcr.microsoft.com/mssql/server:2022-latest", sqlAttributes.GetProperty(ResourceAttributeNames.ContainerImage).GetString());
        Assert.Contains(
            "volume:application-topology-sql-data",
            sqlServer.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("secrets.vault", secrets.GetProperty("typeId").GetString());

        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, api.GetProperty("typeId").GetString());
        Assert.Equal("../Api/CloudShell.ApplicationTopologyApi.csproj", apiAttributes.GetProperty(ResourceAttributeNames.ProjectPath).GetString());
        Assert.Contains(
            "application:application-topology-sql-server",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "configuration:application-topology",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "secrets-vault:application-topology",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, frontend.GetProperty("typeId").GetString());
        Assert.Equal($"http://localhost:{frontendPort}", GetEndpointAddress(frontend, "http"));
        Assert.Equal("../Frontend/CloudShell.ApplicationTopologyFrontend.csproj", frontendAttributes.GetProperty(ResourceAttributeNames.ProjectPath).GetString());
        Assert.Contains(
            "application:application-topology-api",
            frontend.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        Assert.Equal(PlatformResourceProvider.DnsZoneResourceType, dnsZone.GetProperty("typeId").GetString());
        Assert.Equal(
            "application-topology.cloudshell.local",
            dnsZoneAttributes.GetProperty(ResourceAttributeNames.DnsZoneName).GetString());
        Assert.Equal("local-hostnames", dnsZoneAttributes.GetProperty(ResourceAttributeNames.DnsProvider).GetString());
        Assert.Equal("1", dnsZoneAttributes.GetProperty(ResourceAttributeNames.DnsRecordCount).GetString());
        Assert.Equal("dns:application-topology-local", nameMapping.GetProperty("parentResourceId").GetString());
        Assert.Equal(
            "application:application-topology-frontend",
            nameMappingAttributes.GetProperty(ResourceAttributeNames.NameMappingTargetResourceId).GetString());
        Assert.Equal(
            "http",
            nameMappingAttributes.GetProperty(ResourceAttributeNames.NameMappingTargetEndpointName).GetString());
        Assert.Equal(
            "ProviderSelected",
            nameMappingAttributes.GetProperty(ResourceAttributeNames.NameMappingMaterializationStatus).GetString());

        var storageVolumesHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("storage:application-topology-local")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Volumes.Value)}");
        Assert.Contains("Add volume", storageVolumesHtml);
        Assert.Contains("This Storage resource cannot be deleted while it owns volumes.", storageVolumesHtml);

        var apiEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Application exposure", apiEndpointsHtml);
        Assert.Contains("Add load-balancer route", apiEndpointsHtml);
        Assert.Contains("Add name mapping", apiEndpointsHtml);
        Assert.Contains("type=cloudshell.loadBalancer", apiEndpointsHtml);
        Assert.Contains("targetResourceId=application%3Aapplication-topology-api", apiEndpointsHtml);
        Assert.Contains("targetEndpointName=http", apiEndpointsHtml);
        Assert.Contains("returnUrl=%2Fresources%2Fapplication%253Aapplication-topology-api%2Fdetails%3Ftab%3Dnetworking%253Aendpoints", apiEndpointsHtml);

        var sqlEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Application exposure", sqlEndpointsHtml);
        Assert.Contains("type=cloudshell.loadBalancer", sqlEndpointsHtml);
        Assert.Contains("targetResourceId=application%3Aapplication-topology-sql-server", sqlEndpointsHtml);
        Assert.Contains("targetEndpointName=tds", sqlEndpointsHtml);
        Assert.Contains("routeKind=tcp", sqlEndpointsHtml);
        Assert.Contains("returnUrl=%2Fresources%2Fapplication%253Aapplication-topology-sql-server%2Fdetails%3Ftab%3Dnetworking%253Aendpoints", sqlEndpointsHtml);

        var sqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details");
        AssertResourceTabsInOrder(
            sqlDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Endpoints<",
            ">DNS<",
            ">Deployment<",
            ">Replicas<",
            ">Storage<",
            ">Environment<",
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

        var settingsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("configuration:sample-app")}/details");
        AssertResourceTabsInOrder(
            settingsDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Entries<",
            ">Endpoints<");
        Assert.DoesNotContain(">Settings<", settingsDetailsHtml);
        Assert.DoesNotContain("aria-label=\"Entries\"", settingsDetailsHtml);

        var secretsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets-vault:sample-app")}/details");
        AssertResourceTabsInOrder(
            secretsDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Secrets<",
            ">Endpoints<");
        Assert.DoesNotContain(">Settings<", secretsDetailsHtml);
        Assert.DoesNotContain("aria-label=\"Secrets\"", secretsDetailsHtml);

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
    public async Task ThirdPartyIdentitySample_KeycloakProvisionedWorkloadReadsConfiguration()
    {
        if (!await DockerComposeStack.IsAvailableAsync())
        {
            return;
        }

        var keycloakPort = await GetFreePortAsync();
        var apiPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:third-party-identity");
        var root = SampleProcess.FindRepositoryRoot();
        var projectName = $"cloudshell-third-party-identity-test-{Guid.NewGuid():N}";
        using var keycloak = await DockerComposeStack.StartAsync(
            root,
            "samples/ThirdPartyIdentity/docker-compose.yml",
            projectName,
            [("KEYCLOAK_PORT", keycloakPort.ToString(CultureInfo.InvariantCulture))]);

        var authority = $"http://localhost:{keycloakPort}/realms/cloudshell";
        await WaitForHttpSuccessAsync(
            $"{authority}/.well-known/openid-configuration",
            TimeSpan.FromMinutes(2));

        using var host = await SampleProcess.StartAsync(
            "samples/ThirdPartyIdentity/CloudShell.ThirdPartyIdentity.csproj",
            await GetFreePortAsync(),
            [
                ("Authentication__Enabled", "false"),
                ("Authentication__OpenIdConnect__Authority", authority),
                ("Authentication__OpenIdConnect__RequireHttpsMetadata", "false"),
                ("Keycloak__AdminBaseAddress", $"http://localhost:{keycloakPort}"),
                ("Keycloak__TokenEndpoint", $"{authority}/protocol/openid-connect/token"),
                ("Samples__ThirdPartyIdentity__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("Samples__ThirdPartyIdentity__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var provisioning = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "identity-provisioning:keycloak");
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:third-party-identity");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:keycloak-provisioned-api");
        var identity = api.GetProperty("identity");

        Assert.Equal(ResourceIdentityProvisioningResources.ResourceType, provisioning.GetProperty("typeId").GetString());
        Assert.Equal("keycloak", provisioning.GetProperty("name").GetString());
        Assert.Equal("Keycloak Identity Provisioning", provisioning.GetProperty("displayName").GetString());
        Assert.Equal(JsonValueKind.Null, provisioning.GetProperty("state").ValueKind);
        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("identity:keycloak", identity.GetProperty("providerId").GetString());
        Assert.Equal("keycloak-provisioned-api", identity.GetProperty("name").GetString());
        Assert.Contains(
            "configuration:third-party-identity",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        var provisioningStatusJson = await host.GetStringAsync(
            "/api/control-plane/v1/resources/application%3Akeycloak-provisioned-api/identity/provisioning-status");
        using var provisioningStatusDocument = JsonDocument.Parse(provisioningStatusJson);
        Assert.Equal(
            "identity:keycloak",
            provisioningStatusDocument.RootElement.GetProperty("providerId").GetString());
        var provisioningStatus = Assert.Single(
            provisioningStatusDocument.RootElement.GetProperty("statuses").EnumerateArray());
        var provisioningState = provisioningStatus.GetProperty("state");
        if (provisioningState.ValueKind == JsonValueKind.String)
        {
            Assert.Equal("provisioned", provisioningState.GetString()?.ToLowerInvariant());
        }
        else
        {
            Assert.Equal((int)ResourceIdentityProvisioningState.Provisioned, provisioningState.GetInt32());
        }

        await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Akeycloak-provisioned-api/actions/start?startDependencies=true");

        var configurationJson = await WaitForJsonStatusAsync(
            $"http://localhost:{apiPort}/configuration",
            "connected",
            TimeSpan.FromMinutes(1));
        using var configurationDocument = JsonDocument.Parse(configurationJson);
        var configurationRoot = configurationDocument.RootElement;

        Assert.Equal("connected", configurationRoot.GetProperty("status").GetString());
        Assert.Equal("cloudshell-keycloak-provisioned-api", configurationRoot.GetProperty("clientId").GetString());
        Assert.Contains(
            configurationRoot.GetProperty("entries").EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a Keycloak-provisioned resource identity");
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
            $"/resources/{Uri.EscapeDataString("sample:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Activity.Value)}");
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
        var registryPort = await GetFreePortAsync();
        using var host = await SampleProcess.StartAsync(
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            await GetFreePortAsync(),
            [
                ("ContainerAppDeployment__RegistryPort", registryPort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:sample-api");
        var registry = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:container:sample-registry");

        var registryAddress = $"localhost:{registryPort.ToString(CultureInfo.InvariantCulture)}";
        Assert.Equal(registryAddress, app.GetProperty("attributes").GetProperty("container.registry").GetString());
        Assert.Equal(registryAddress, registry.GetProperty("attributes").GetProperty("container.registry").GetString());

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
        Assert.Equal("http://localhost:5290", GetEndpointAddress(network, "api-public"));
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

    private static async Task WaitForHttpSuccessAsync(string url, TimeSpan timeout)
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
            try
            {
                using var response = await client.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                lastException = exception;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Endpoint '{url}' did not become ready within {timeout}." +
            $"{Environment.NewLine}{lastStatus ?? lastException?.Message}");
    }

    private static async Task<string> WaitForJsonStatusAsync(
        string url,
        string expectedStatus,
        TimeSpan timeout)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;
        string? lastBody = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                lastBody = await client.GetStringAsync(url);
                using var document = JsonDocument.Parse(lastBody);
                if (string.Equals(
                        document.RootElement.GetProperty("status").GetString(),
                        expectedStatus,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return lastBody;
                }
            }
            catch (Exception exception) when (
                exception is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException)
            {
                lastException = exception;
            }

            await Task.Delay(500);
        }

        throw new TimeoutException(
            $"Endpoint '{url}' did not return status '{expectedStatus}' within {timeout}." +
            $"{Environment.NewLine}{lastBody ?? lastException?.Message}");
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
        var endpointNetworkMapping = resource
            .GetProperty("endpointNetworkMappings")
            .EnumerateArray()
            .Single(mapping =>
                string.Equals(mapping.GetProperty("name").GetString(), endpointName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapping.GetProperty("target").GetProperty("endpointName").GetString(), endpointName, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(mapping.GetProperty("sourceEndpointName").GetString(), endpointName, StringComparison.OrdinalIgnoreCase));

        return endpointNetworkMapping.GetProperty("address").GetString() ??
            throw new InvalidOperationException($"Endpoint '{endpointName}' did not include an endpoint network mapping address.");
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

    private sealed class DockerComposeStack : IDisposable
    {
        private readonly string root;
        private readonly string composeFile;
        private readonly string projectName;
        private bool disposed;

        private DockerComposeStack(string root, string composeFile, string projectName)
        {
            this.root = root;
            this.composeFile = composeFile;
            this.projectName = projectName;
        }

        public static async Task<bool> IsAvailableAsync()
        {
            try
            {
                var result = await RunDockerAsync(
                    SampleProcess.FindRepositoryRoot(),
                    ["compose", "version"],
                    null,
                    TimeSpan.FromSeconds(10),
                    throwOnError: false);
                return result.ExitCode == 0;
            }
            catch
            {
                return false;
            }
        }

        public static async Task<DockerComposeStack> StartAsync(
            string root,
            string composeFile,
            string projectName,
            IReadOnlyList<(string Key, string Value)> environment)
        {
            var stack = new DockerComposeStack(root, composeFile, projectName);
            await RunDockerAsync(
                root,
                ["compose", "-f", composeFile, "-p", projectName, "up", "-d"],
                environment,
                TimeSpan.FromMinutes(3),
                throwOnError: true);
            return stack;
        }

        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                RunDockerAsync(
                        root,
                        ["compose", "-f", composeFile, "-p", projectName, "down", "-v", "--remove-orphans"],
                        null,
                        TimeSpan.FromMinutes(1),
                        throwOnError: false)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Test cleanup should not hide the original test failure.
            }
        }

        private static async Task<ProcessResult> RunDockerAsync(
            string workingDirectory,
            IReadOnlyList<string> arguments,
            IReadOnlyList<(string Key, string Value)>? environment,
            TimeSpan timeout,
            bool throwOnError)
        {
            var output = new StringBuilder();
            var startInfo = new ProcessStartInfo("docker")
            {
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            foreach (var argument in arguments)
            {
                startInfo.ArgumentList.Add(argument);
            }

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException("Could not start Docker.");
            var outputTask = CaptureAsync(process.StandardOutput, output);
            var errorTask = CaptureAsync(process.StandardError, output);
            try
            {
                await process.WaitForExitAsync().WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }

                throw;
            }

            await Task.WhenAll(outputTask, errorTask);

            var result = new ProcessResult(process.ExitCode, output.ToString());
            if (throwOnError && result.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Docker command failed with exit code {result.ExitCode}.{Environment.NewLine}{result.Output}");
            }

            return result;

            static async Task CaptureAsync(StreamReader reader, StringBuilder output)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    lock (output)
                    {
                        output.AppendLine(line);
                    }
                }
            }
        }

        private sealed record ProcessResult(int ExitCode, string Output);
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
            var content = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).{Environment.NewLine}{content}");
            }

            return content;
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
