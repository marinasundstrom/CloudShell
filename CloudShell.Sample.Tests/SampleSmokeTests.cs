using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ApplicationTopology.ServiceDefaults;
using CloudShell.ContainerHost;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.Providers.Applications;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SqlServerResources = CloudShell.Providers.Applications.ApplicationProviderServiceCollectionExtensions;

namespace CloudShell.Sample.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SampleSmokeCollection
{
    public const string Name = "Sample smoke tests";
}

[Collection(SampleSmokeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SampleSmokeTests
{
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);

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
        var applicationProvider = ActivatorUtilities.CreateInstance<ApplicationResourceService>(serviceProvider);
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

        Assert.Equal(ApplicationResourceTypes.SqlServer, sqlServer.EffectiveTypeId);
        Assert.Equal(ResourceClass.Service, sqlServer.ResourceClass);
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
        var frontendPort = await GetFreePortAsync();
        var frontendEndpoint = $"http://127.0.0.1:{frontendPort}";
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync(),
            [
                ("ProjectReference__FrontendEndpoint", frontendEndpoint)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Project Reference API", resourcesHtml);
        Assert.Contains("Project Reference Frontend", resourcesHtml);
        Assert.Contains("href=\"/resources/graph\"", resourcesHtml);

        var resourceGraphHtml = await host.GetStringAsync("/resources/graph");
        Assert.Contains("Resource graph", resourceGraphHtml);
        Assert.Contains("resource-dependency-graph-canvas", resourceGraphHtml);

        var apiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var document = JsonDocument.Parse(apiJson);
        var resources = document.RootElement.EnumerateArray().ToArray();
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-api");
        Assert.Contains(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-frontend");

        await host.WaitForAbsoluteHttpOkAsync(
            $"{frontendEndpoint}/upstream",
            bearerToken: null,
            StartupTimeout);
        var upstreamJson = await host.GetAbsoluteStringAsync($"{frontendEndpoint}/upstream");
        Assert.Contains("Project Reference Frontend", upstreamJson);
        Assert.Contains("Hello from the referenced API project.", upstreamJson);

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
                },
                {
                  "traceId": "{{traceId}}",
                  "spanId": "00f067aa0ba902b8",
                  "parentSpanId": "00f067aa0ba902b7",
                  "name": "GET /failure",
                  "resourceId": "application:project-reference-api",
                  "serviceName": "project-reference-api",
                  "kind": "Server",
                  "status": "Error",
                  "startTime": "2026-06-16T00:00:00.025Z",
                  "duration": "00:00:00.0500000",
                  "attributes": {
                    "http.route": "/failure",
                    "error.type": "500"
                  }
                }
              ]
            }
            """);

        await host.SendJsonAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/metrics/ingest",
            """
            {
              "points": [
                {
                  "name": "http.server.requests",
                  "resourceId": "application:project-reference-frontend",
                  "serviceName": "project-reference-frontend",
                  "value": 1,
                  "timestamp": "2026-06-16T00:00:01Z",
                  "unit": "count",
                  "attributes": {
                    "http.method": "GET",
                    "http.route": "/upstream",
                    "http.status_code": "200"
                  }
                },
                {
                  "name": "http.server.duration",
                  "resourceId": "application:project-reference-frontend",
                  "serviceName": "project-reference-frontend",
                  "value": 125,
                  "timestamp": "2026-06-16T00:00:01Z",
                  "unit": "ms",
                  "attributes": {
                    "http.method": "GET",
                    "http.route": "/upstream",
                    "http.status_code": "200"
                  }
                }
              ]
            }
            """);

        var frontendOverviewHtml = await host.GetStringAsync(
            "/resources/application%3Aproject-reference-frontend/details");
        Assert.Contains("Dependency graph", frontendOverviewHtml);
        Assert.Contains("Project Reference API", frontendOverviewHtml);
        Assert.Contains(
            "href=\"/resources/application%3Aproject-reference-api/activity\"",
            frontendOverviewHtml);
        Assert.Contains(
            "href=\"/resources/application%3Aproject-reference-api/traces\"",
            frontendOverviewHtml);

        var traceHtml = await host.GetStringAsync(
            $"/observability/traces?resourceId=application%3Aproject-reference-frontend&traceId={traceId}");
        Assert.Contains("Trace chart", traceHtml);
        Assert.Contains("id=\"trace-source-filter\"", traceHtml);
        Assert.Contains("Project Reference Frontend", traceHtml);
        Assert.Contains("Related logs", traceHtml);
        Assert.Contains("Related activity", traceHtml);
        Assert.Contains("Open resource", traceHtml);
        Assert.Contains("<fluent-anchor", traceHtml);
        Assert.Contains(">Project Reference Frontend</a>", traceHtml);
        Assert.DoesNotContain(">application:project-reference-frontend</a>", traceHtml);
        Assert.DoesNotContain("id=\"trace-sort-mode\"", traceHtml);
        Assert.Contains("Error spans", traceHtml);
        Assert.Contains("trace-span-row selected attention", traceHtml);
        Assert.Contains("trace-attention-pill", traceHtml);
        Assert.Contains("Needs attention", traceHtml);
        Assert.Contains("trace-span-row error", traceHtml);
        Assert.Contains("trace-error-pill", traceHtml);
        Assert.Contains(
            "href=\"/resources/application%3Aproject-reference-frontend/logs?traceId=4bf92f3577b34da6a3ce929d0e0e4736\"",
            traceHtml);
        Assert.Contains(
            "href=\"/resources/application%3Aproject-reference-frontend/activity?traceId=4bf92f3577b34da6a3ce929d0e0e4736&amp;spanId=00f067aa0ba902b7\"",
            traceHtml);
        Assert.Contains("href=\"/resources/application%3Aproject-reference-frontend\"", traceHtml);

        var traceListHtml = await host.GetStringAsync(
            "/observability/traces?resourceId=application%3Aproject-reference-api");
        Assert.Contains("id=\"trace-sort-mode\"", traceListHtml);
        Assert.Contains("<dt>Resource name</dt>", traceListHtml);
        Assert.Contains("<dd>project-reference-api</dd>", traceListHtml);
        Assert.Contains("<dt>Canonical resource ID</dt>", traceListHtml);
        Assert.Contains("<dd>application:project-reference-api</dd>", traceListHtml);
        Assert.Contains("Newest", traceListHtml);
        Assert.Contains("Longest duration", traceListHtml);
        Assert.Contains("Errors first", traceListHtml);
        Assert.Contains("recent-trace-item error", traceListHtml);
        Assert.Contains("1 error span(s)", traceListHtml);

        var allTraceListHtml = await host.GetStringAsync("/observability/traces");
        Assert.Contains("All sources", allTraceListHtml);
        Assert.Contains("2 trace resources", allTraceListHtml);
        Assert.Contains("GET /upstream", allTraceListHtml);
        Assert.Contains("project-reference-frontend, project-reference-api", allTraceListHtml);
        Assert.Contains("recent-trace-item attention", allTraceListHtml);
        Assert.Contains("Needs attention: 1 error span(s)", allTraceListHtml);
        Assert.Contains(
            $"href=\"/observability/traces?resourceId=application%3Aproject-reference-frontend&amp;traceId={traceId}\"",
            allTraceListHtml);

        var serviceMapHtml = await host.GetStringAsync("/observability/service-map");
        Assert.Contains("Service map", serviceMapHtml);
        Assert.Contains("Project Reference Frontend", serviceMapHtml);

        var missingTraceResourceHtml = await host.GetStringAsync(
            "/observability/traces?resourceId=application%3Aproject-reference-missing");
        Assert.Contains("Trace resource not found", missingTraceResourceHtml);
        Assert.Contains("project-reference-missing", missingTraceResourceHtml);
        Assert.DoesNotContain("application:project-reference-missing", missingTraceResourceHtml);
        Assert.Contains("All trace resources", missingTraceResourceHtml);

        var missingTraceScopeHtml = await host.GetStringAsync(
            "/observability/traces?resourceId=application%3Aproject-reference-frontend&scopeResourceId=application%3Aproject-reference-missing-scope");
        Assert.Contains("Trace scope not found", missingTraceScopeHtml);
        Assert.Contains("project-reference-missing-scope", missingTraceScopeHtml);
        Assert.DoesNotContain("application:project-reference-missing-scope", missingTraceScopeHtml);
        Assert.Contains("Show all scopes", missingTraceScopeHtml);

        var relatedLogsHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&traceId={traceId}");
        Assert.Contains("Telemetry", relatedLogsHtml);
        Assert.DoesNotContain("Resource telemetry", relatedLogsHtml);
        Assert.Contains("Project Reference Frontend", relatedLogsHtml);
        Assert.Contains("Console logs", relatedLogsHtml);
        Assert.Contains("id=\"log-source-filter\"", relatedLogsHtml);
        Assert.Contains("Showing entries correlated with trace", relatedLogsHtml);
        Assert.Contains("Clear trace filter", relatedLogsHtml);

        var relatedTracesHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Traces.Value)}&traceId={traceId}");
        Assert.Contains("Telemetry", relatedTracesHtml);
        Assert.DoesNotContain("Resource telemetry", relatedTracesHtml);
        Assert.Contains("Trace chart", relatedTracesHtml);
        Assert.DoesNotContain("id=\"trace-source-filter\"", relatedTracesHtml);
        Assert.Contains("Related logs", relatedTracesHtml);
        Assert.Contains("Related activity", relatedTracesHtml);
        Assert.Contains(">Project Reference Frontend</a>", relatedTracesHtml);
        Assert.DoesNotContain(">application:project-reference-frontend</a>", relatedTracesHtml);
        Assert.Contains("Clear trace filter", relatedTracesHtml);
        Assert.Contains(
            "href=\"/resources/application%3Aproject-reference-frontend/logs?traceId=4bf92f3577b34da6a3ce929d0e0e4736\"",
            relatedTracesHtml);

        var missingInlineTraceScopeHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Traces.Value)}&scopeResourceId=application%3Aproject-reference-missing-scope");
        Assert.Contains("Trace scope not found", missingInlineTraceScopeHtml);
        Assert.Contains("project-reference-missing-scope", missingInlineTraceScopeHtml);
        Assert.DoesNotContain("application:project-reference-missing-scope", missingInlineTraceScopeHtml);

        var relatedMetricsHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}");
        Assert.Contains("Telemetry", relatedMetricsHtml);
        Assert.DoesNotContain("Resource telemetry", relatedMetricsHtml);
        Assert.DoesNotContain("Metric source", relatedMetricsHtml);
        Assert.DoesNotContain("id=\"metric-source-filter\"", relatedMetricsHtml);
        Assert.Contains("http.server.requests", relatedMetricsHtml);
        Assert.Contains("http.server.duration", relatedMetricsHtml);
        Assert.Contains("project-reference-frontend", relatedMetricsHtml);

        var missingMetricResourceHtml = await host.GetStringAsync(
            "/observability/metrics?resourceId=application%3Aproject-reference-missing");
        Assert.Contains("Metric resource not found", missingMetricResourceHtml);
        Assert.Contains("project-reference-missing", missingMetricResourceHtml);
        Assert.DoesNotContain("application:project-reference-missing", missingMetricResourceHtml);
        Assert.Contains("All metric resources", missingMetricResourceHtml);

        var missingMetricScopeHtml = await host.GetStringAsync(
            "/observability/metrics?resourceId=application%3Aproject-reference-frontend&scopeResourceId=application%3Aproject-reference-missing-scope");
        Assert.Contains("Metric scope not found", missingMetricScopeHtml);
        Assert.Contains("project-reference-missing-scope", missingMetricScopeHtml);
        Assert.DoesNotContain("application:project-reference-missing-scope", missingMetricScopeHtml);
        Assert.Contains("Project Reference Frontend", missingMetricScopeHtml);
        Assert.Contains("Show all scopes", missingMetricScopeHtml);

        var missingInlineMetricScopeHtml = await host.GetStringAsync(
            $"/resources/application%3Aproject-reference-frontend/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}&scopeResourceId=application%3Aproject-reference-missing-scope");
        Assert.Contains("Metric scope not found", missingInlineMetricScopeHtml);
        Assert.Contains("project-reference-missing-scope", missingInlineMetricScopeHtml);
        Assert.DoesNotContain("application:project-reference-missing-scope", missingInlineMetricScopeHtml);

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

        var resourceDetailsHtml = await host.GetStringAsync("/resources/application%3Aproject-reference-api/details");
        Assert.Contains("Stop unavailable. Resource Manager is in read-only mode.", resourceDetailsHtml);

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

        var missingTypeHtml = await host.GetStringAsync(
            "/resources/add?type=application.does-not-exist");
        Assert.Contains("Resource type not found", missingTypeHtml);
        Assert.Contains("application.does-not-exist", missingTypeHtml);
        Assert.Contains("Show resource types", missingTypeHtml);
    }

    [Fact]
    public async Task ApplicationTopologyHost_ProjectsSqlStorageAndServiceDiscoveryTopology()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
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
        var sqlDatabase = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-sql-server/database:application-topology");
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
        var sqlDatabaseAttributes = sqlDatabase.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");
        var frontendAttributes = frontend.GetProperty("attributes");
        var dnsZoneAttributes = dnsZone.GetProperty("attributes");
        var nameMappingAttributes = nameMapping.GetProperty("attributes");
        var settingsIdentity = settings.GetProperty("identity");
        var secretsIdentity = secrets.GetProperty("identity");
        var apiIdentity = api.GetProperty("identity");

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

        Assert.Equal(ApplicationResourceTypes.SqlServer, sqlServer.GetProperty("typeId").GetString());
        Assert.Equal($"tcp://localhost:{sqlPort}", GetEndpointAddress(sqlServer, "tds"));
        Assert.Equal("1", sqlAttributes.GetProperty(ResourceAttributeNames.DatabaseCount).GetString());
        Assert.Equal("1", sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountCount).GetString());
        Assert.Equal("0", sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountMaterializedCount).GetString());
        Assert.Equal(
            ResourceVolumeMountMaterializationStatus.NotActive,
            sqlAttributes.GetProperty(ResourceAttributeNames.VolumeMountMaterializationStatus).GetString());
        Assert.Equal(SqlServerResources.DefaultSqlServerImage, sqlAttributes.GetProperty(ResourceAttributeNames.ContainerImage).GetString());
        Assert.Contains(
            "volume:application-topology-sql-data",
            sqlServer.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        var sqlIdentity = sqlServer.GetProperty("identity");
        Assert.Equal("identity:development", sqlIdentity.GetProperty("providerId").GetString());
        Assert.Equal("application-topology-sql-server", sqlIdentity.GetProperty("name").GetString());
        Assert.Equal(ApplicationResourceTypes.SqlDatabase, sqlDatabase.GetProperty("typeId").GetString());
        Assert.Equal("application:application-topology-sql-server", sqlDatabase.GetProperty("parentResourceId").GetString());
        Assert.Equal("Application Topology", sqlDatabase.GetProperty("displayName").GetString());
        Assert.Equal("application_topology", sqlDatabaseAttributes.GetProperty(ResourceAttributeNames.DatabaseName).GetString());
        Assert.Equal("application:application-topology-sql-server", sqlDatabaseAttributes.GetProperty(ResourceAttributeNames.DatabaseServerResourceId).GetString());
        Assert.Equal("declared", sqlDatabaseAttributes.GetProperty(ResourceAttributeNames.DatabaseSource).GetString());

        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("secrets.vault", secrets.GetProperty("typeId").GetString());
        Assert.Equal("identity:development", settingsIdentity.GetProperty("providerId").GetString());
        Assert.Equal("identity:development", secretsIdentity.GetProperty("providerId").GetString());

        Assert.Equal(ApplicationResourceTypes.AspNetCoreProject, api.GetProperty("typeId").GetString());
        Assert.Equal("../Api/CloudShell.ApplicationTopologyApi.csproj", apiAttributes.GetProperty(ResourceAttributeNames.ProjectPath).GetString());
        Assert.Equal(
            ResourceDeclarationPersistence.Transient.ToString(),
            apiAttributes.GetProperty(ResourceAttributeNames.DeclarationPersistence).GetString());
        Assert.Equal("identity:development", apiIdentity.GetProperty("providerId").GetString());
        Assert.Equal("application-topology-api", apiIdentity.GetProperty("name").GetString());
        Assert.Contains(
            "application:application-topology-sql-server",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "configuration:application-topology",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "secrets-vault:application-topology",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        var apiRecoveryPolicyJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application:application-topology-api")}/recovery-policy");
        using var apiRecoveryPolicyDocument = JsonDocument.Parse(apiRecoveryPolicyJson);
        var apiRecoveryPolicy = apiRecoveryPolicyDocument.RootElement;
        Assert.True(apiRecoveryPolicy.GetProperty("enabled").GetBoolean());
        Assert.Equal((int)ResourceProbeType.Liveness, apiRecoveryPolicy.GetProperty("probeType").GetInt32());
        Assert.Equal(3, apiRecoveryPolicy.GetProperty("failureThreshold").GetInt32());
        Assert.Equal(3, apiRecoveryPolicy.GetProperty("maxAttempts").GetInt32());

        var apiRecoveryStatusJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application:application-topology-api")}/recovery-status");
        using var apiRecoveryStatusDocument = JsonDocument.Parse(apiRecoveryStatusJson);
        var apiRecoveryStatus = apiRecoveryStatusDocument.RootElement;
        Assert.Equal((int)ResourceRecoveryState.WaitingForSignal, apiRecoveryStatus.GetProperty("state").GetInt32());
        Assert.True(apiRecoveryStatus.GetProperty("policy").GetProperty("enabled").GetBoolean());

        var grantsJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants" +
            $"?principalKind={(int)ResourcePrincipalKind.ResourceIdentity}" +
            $"&principalId={Uri.EscapeDataString("application:application-topology-api/identities/application-topology-api")}");
        using var grantsDocument = JsonDocument.Parse(grantsJson);
        var grants = grantsDocument.RootElement.EnumerateArray().ToArray();
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "secrets-vault:application-topology" &&
                grant.GetProperty("permission").GetString() == SecretsVaultResourceOperationPermissions.ReadSecrets);
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "configuration:application-topology" &&
                grant.GetProperty("permission").GetString() == ConfigurationStoreResourceOperationPermissions.ReadEntries);
        Assert.Contains(
            grants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "application:application-topology-sql-server" &&
                grant.GetProperty("permission").GetString() == DatabaseResourceOperationPermissions.ReadWrite);

        await AssertProvisionedIdentityStatusAsync(host, "application:application-topology-api");
        await AssertProvisionedIdentityStatusAsync(host, "application:application-topology-sql-server");
        await AssertProvisionedIdentityStatusAsync(host, "configuration:application-topology");
        await AssertProvisionedIdentityStatusAsync(host, "secrets-vault:application-topology");

        var resourceToken = await host.GetClientCredentialsTokenAsync(
            "application:application-topology-api/application-topology-api",
            "local-development-application-topology-api-secret",
            "ControlPlane.Access");
        Assert.NotEmpty(resourceToken);

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
        var reconcileNameMappingsAction = dnsZone
            .GetProperty("resourceActions")
            .GetProperty("reconcileNameMappings");
        Assert.Equal("Reconcile name mappings", reconcileNameMappingsAction.GetProperty("displayName").GetString());
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

        var addSqlServerHtml = await host.GetStringAsync(
            $"/resources/add?type={Uri.EscapeDataString(ApplicationResourceTypes.SqlServer)}");
        Assert.Contains("SA password", addSqlServerHtml);
        Assert.Contains("TDS endpoint", addSqlServerHtml);
        Assert.Contains("Advanced runtime settings", addSqlServerHtml);
        Assert.DoesNotContain("Container host", addSqlServerHtml);
        Assert.DoesNotContain("Container image", addSqlServerHtml);
        Assert.DoesNotContain("Registry username", addSqlServerHtml);
        Assert.DoesNotContain("Dockerfile", addSqlServerHtml);

        var sqlConfigurationHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Configuration.Value)}");
        Assert.Contains("SA password", sqlConfigurationHtml);
        Assert.Contains("TDS endpoint assignment", sqlConfigurationHtml);
        Assert.Contains("Advanced runtime settings", sqlConfigurationHtml);
        Assert.DoesNotContain("Container host", sqlConfigurationHtml);
        Assert.DoesNotContain("Container image", sqlConfigurationHtml);
        Assert.DoesNotContain("Registry username", sqlConfigurationHtml);
        Assert.DoesNotContain("Dockerfile", sqlConfigurationHtml);
        Assert.DoesNotContain("Scale and replicas", sqlConfigurationHtml);

        var apiEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Application exposure", apiEndpointsHtml);
        Assert.Contains("Add load-balancer route", apiEndpointsHtml);
        Assert.Contains("Add name mapping", apiEndpointsHtml);
        Assert.Contains("type=cloudshell.loadBalancer", apiEndpointsHtml);
        Assert.Contains("targetResourceId=application%3Aapplication-topology-api", apiEndpointsHtml);
        Assert.Contains("targetEndpointName=http", apiEndpointsHtml);
        Assert.Contains("returnUrl=%2Fresources%2Fapplication%253Aapplication-topology-api%2Fendpoints", apiEndpointsHtml);

        var apiDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details");
        Assert.Contains(">Identity<", apiDetailsHtml);
        Assert.Contains("ASP.NET Core project / application-topology-api", apiDetailsHtml);
        Assert.DoesNotContain("ASP.NET Core project / application:application-topology-api", apiDetailsHtml);
        Assert.Contains("Action readiness", apiDetailsHtml);
        Assert.Contains("Start preflight checks passed.", apiDetailsHtml);
        Assert.Contains("Resource status", apiDetailsHtml);
        Assert.Contains("Unhealthy", apiDetailsHtml);
        Assert.Contains("Checked", apiDetailsHtml);
        Assert.Contains("Resource health", apiDetailsHtml);
        Assert.Contains("Startup declaration", apiDetailsHtml);
        Assert.Contains("Declared by code for this host process.", apiDetailsHtml);
        Assert.Contains("UI changes are temporary until the resource is persisted.", apiDetailsHtml);
        Assert.Contains(">Dependency graph<", apiDetailsHtml);
        Assert.Contains(">Depends on<", apiDetailsHtml);
        Assert.Contains(">Used by<", apiDetailsHtml);
        Assert.Contains("application-topology-sql-server", apiDetailsHtml);
        Assert.Contains("application-topology-frontend", apiDetailsHtml);
        Assert.Contains(">Settings<", apiDetailsHtml);
        Assert.Contains(">Secrets<", apiDetailsHtml);
        Assert.Contains("application.sql-server", apiDetailsHtml);
        Assert.Contains("Environment references", apiDetailsHtml);
        Assert.Contains("ApplicationTopology__Message", apiDetailsHtml);
        Assert.Contains("Configuration entry", apiDetailsHtml);
        Assert.Contains("Settings / ApplicationTopology:Message", apiDetailsHtml);
        Assert.Contains($"Settings; requires {ConfigurationStoreResourceOperationPermissions.ReadEntries} for application-topology-api", apiDetailsHtml);
        Assert.Contains("ApplicationTopology__SqlServer__Password", apiDetailsHtml);
        Assert.Contains("ApplicationTopology__ExternalApiKey", apiDetailsHtml);
        Assert.Contains("Secret reference", apiDetailsHtml);
        Assert.Contains("Secrets / external-api-key", apiDetailsHtml);
        Assert.Contains($"Secrets; requires {SecretsVaultResourceOperationPermissions.ReadSecrets} for application-topology-api", apiDetailsHtml);
        Assert.DoesNotContain("configuration:application-topology; requires", apiDetailsHtml);
        Assert.DoesNotContain("secrets-vault:application-topology; requires", apiDetailsHtml);
        Assert.Contains(">Hidden<", apiDetailsHtml);
        Assert.Contains(">Granted<", apiDetailsHtml);
        Assert.Contains("Resource identity", apiDetailsHtml);
        Assert.Contains("Access control", apiDetailsHtml);
        Assert.Contains("application-topology-api", apiDetailsHtml);
        Assert.Contains("Provider: identity:development", apiDetailsHtml);
        Assert.Contains("Provisioned", apiDetailsHtml);
        Assert.Contains("Built-in resource identity client is registered.", apiDetailsHtml);
        Assert.Contains(ConfigurationStoreResourceOperationPermissions.ReadEntries, apiDetailsHtml);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, apiDetailsHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/logs", apiDetailsHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/traces\"", apiDetailsHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/recovery\"", apiDetailsHtml);
        Assert.DoesNotContain("href=\"/logs?resourceId=application%3Aapplication-topology-api", apiDetailsHtml);
        Assert.DoesNotContain("href=\"/observability/traces?resourceId=application%3Aapplication-topology-api", apiDetailsHtml);
        Assert.DoesNotContain("CloudShell-Passw0rd!", apiDetailsHtml);
        Assert.DoesNotContain("local-development-api-key", apiDetailsHtml);

        var healthHtml = await host.GetStringAsync("/health");
        Assert.Contains("Review configured resource health checks", healthHtml);
        Assert.Contains("application-topology-api", healthHtml);
        Assert.Contains("application-topology-frontend", healthHtml);
        Assert.Contains("/health", healthHtml);
        Assert.Contains("/healthz", healthHtml);
        Assert.Contains("Unhealthy", healthHtml);
        Assert.Contains("Recent polling", healthHtml);

        var apiHealthHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Health.Value)}");
        Assert.Contains(">Health<", apiHealthHtml);
        Assert.Contains("Health summary", apiHealthHtml);
        Assert.Contains("Auto-refresh", apiHealthHtml);
        Assert.Contains("Recent polling", apiHealthHtml);
        Assert.Contains("Unhealthy", apiHealthHtml);
        Assert.Contains("/health", apiHealthHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/recovery\"", apiHealthHtml);

        var apiRecoveryHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Recovery.Value)}");
        Assert.Contains(">Recovery<", apiRecoveryHtml);
        Assert.Contains("Recovery summary", apiRecoveryHtml);
        Assert.Contains("Waiting for signal", apiRecoveryHtml);
        Assert.Contains("Enabled", apiRecoveryHtml);
        Assert.Contains("Liveness signal", apiRecoveryHtml);
        Assert.Contains("liveness (Liveness)", apiRecoveryHtml);
        Assert.Contains("3 consecutive failed observation(s)", apiRecoveryHtml);
        Assert.Contains("5s initial, 60s max, multiplier 2", apiRecoveryHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/health\"", apiRecoveryHtml);
        Assert.Contains("href=\"/resources/application%3Aapplication-topology-api/activity\"", apiRecoveryHtml);

        var frontendHealthHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Health.Value)}");
        Assert.Contains(">Health<", frontendHealthHtml);
        Assert.Contains("Health summary", frontendHealthHtml);
        Assert.Contains("/healthz", frontendHealthHtml);

        var settingsHtml = await host.GetStringAsync("/settings");
        Assert.Contains(">Settings<", settingsHtml);
        Assert.Contains(">Overview<", settingsHtml);
        Assert.Contains(">Users<", settingsHtml);
        Assert.Contains(">Extensions<", settingsHtml);
        Assert.Contains(">General<", settingsHtml);
        Assert.Contains(">Orchestration<", settingsHtml);
        Assert.Contains(">Resource Management<", settingsHtml);
        Assert.Contains("Settings composition", settingsHtml);

        var usersSettingsHtml = await host.GetStringAsync("/settings/users");
        Assert.Contains(">Users<", usersSettingsHtml);
        Assert.Contains("Settings section", usersSettingsHtml);

        var extensionsSettingsHtml = await host.GetStringAsync("/settings/extensions");
        Assert.Contains(">Extensions<", extensionsSettingsHtml);
        Assert.Contains("Shell extensions", extensionsSettingsHtml);

        var resourceManagerSettingsHtml = await host.GetStringAsync("/settings/resource-manager");
        Assert.Contains(">General<", resourceManagerSettingsHtml);
        Assert.Contains("Resource labels", resourceManagerSettingsHtml);
        Assert.Contains("Inventory visibility", resourceManagerSettingsHtml);

        var resourceManagerOrchestrationSettingsHtml = await host.GetStringAsync("/settings/resource-manager-orchestration");
        Assert.Contains(">Orchestration<", resourceManagerOrchestrationSettingsHtml);
        Assert.Contains(">Orchestrator<", resourceManagerOrchestrationSettingsHtml);
        Assert.Contains("CloudShell modes", resourceManagerOrchestrationSettingsHtml);
        Assert.Contains("Health check interval", resourceManagerOrchestrationSettingsHtml);

        var missingSettingsSectionHtml = await host.GetStringAsync("/settings/does-not-exist");
        Assert.Contains("Section not found", missingSettingsSectionHtml);
        Assert.Contains("Section &#x27;does-not-exist&#x27; is not available.", missingSettingsSectionHtml);
        Assert.Contains("Open overview", missingSettingsSectionHtml);
        Assert.Contains("href=\"/settings\"", missingSettingsSectionHtml);

        var settingsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("configuration:application-topology")}/details");
        Assert.Contains(">Relationships<", settingsDetailsHtml);
        Assert.Contains(">Used by<", settingsDetailsHtml);
        Assert.Contains("application-topology-api", settingsDetailsHtml);

        var settingsIdentityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("configuration:application-topology")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Identity.Value)}");
        Assert.Contains("Enable identity", settingsIdentityHtml);
        Assert.Contains("Provisioned", settingsIdentityHtml);
        Assert.Contains("Built-in resource identity client is registered.", settingsIdentityHtml);
        Assert.Contains("application-topology-api / application-topology-api", settingsIdentityHtml);
        Assert.Contains(ConfigurationStoreResourceOperationPermissions.ReadEntries, settingsIdentityHtml);

        var settingsAccessControlHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("configuration:application-topology")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.AccessControl.Value)}");
        Assert.Contains("Search principals", settingsAccessControlHtml);
        Assert.Contains("Assigned principals", settingsAccessControlHtml);
        Assert.Contains("Configuration entries: read", settingsAccessControlHtml);
        Assert.Contains("application-topology-api", settingsAccessControlHtml);
        Assert.Contains(ConfigurationStoreResourceOperationPermissions.ReadEntries, settingsAccessControlHtml);
        Assert.DoesNotContain("Secrets: read", settingsAccessControlHtml);

        var secretsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets-vault:application-topology")}/details");
        Assert.Contains(">Relationships<", secretsDetailsHtml);
        Assert.Contains(">Used by<", secretsDetailsHtml);
        Assert.Contains("application-topology-api", secretsDetailsHtml);

        var secretsIdentityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets-vault:application-topology")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Identity.Value)}");
        Assert.Contains("Enable identity", secretsIdentityHtml);
        Assert.Contains("Provisioned", secretsIdentityHtml);
        Assert.Contains("Built-in resource identity client is registered.", secretsIdentityHtml);
        Assert.Contains("application-topology-api / application-topology-api", secretsIdentityHtml);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, secretsIdentityHtml);

        var secretsAccessControlHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets-vault:application-topology")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.AccessControl.Value)}");
        Assert.Contains("Search principals", secretsAccessControlHtml);
        Assert.Contains("Assigned principals", secretsAccessControlHtml);
        Assert.Contains("Secrets: read", secretsAccessControlHtml);
        Assert.Contains("application-topology-api", secretsAccessControlHtml);
        Assert.Contains(SecretsVaultResourceOperationPermissions.ReadSecrets, secretsAccessControlHtml);
        Assert.DoesNotContain("Configuration entries: read", secretsAccessControlHtml);

        var apiMonitoringHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value)}");
        Assert.Contains("Auto-refresh", apiMonitoringHtml);
        Assert.Contains(">Monitoring<", apiMonitoringHtml);
        Assert.DoesNotContain(">Refresh<", apiMonitoringHtml);

        var apiEnvironmentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Environment.Value)}");
        Assert.Contains("Startup declaration", apiEnvironmentHtml);
        Assert.Contains("Declared by code for this host process.", apiEnvironmentHtml);
        Assert.Contains("ApplicationTopology__SqlServer__Database", apiEnvironmentHtml);
        Assert.Contains("application_topology", apiEnvironmentHtml);
        Assert.Contains("ApplicationTopology__SqlServer__Authentication", apiEnvironmentHtml);
        Assert.Contains("CloudShell", apiEnvironmentHtml);
        Assert.Contains("CLOUDSHELL_SQL_CREDENTIAL_ENDPOINT", apiEnvironmentHtml);
        Assert.Contains("ApplicationTopology__SqlServer__Password", apiEnvironmentHtml);
        Assert.Contains("Stored value hidden", apiEnvironmentHtml);
        Assert.DoesNotContain("CloudShell-Passw0rd!", apiEnvironmentHtml);

        var frontendAccessControlHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.AccessControl.Value)}");
        Assert.Contains("Search principals", frontendAccessControlHtml);
        Assert.Contains("Assigned principals", frontendAccessControlHtml);
        Assert.Contains("Select a permission", frontendAccessControlHtml);
        Assert.DoesNotContain("Identity required", frontendAccessControlHtml);
        Assert.DoesNotContain("Set up an identity for this resource before assigning access permissions.", frontendAccessControlHtml);
        Assert.DoesNotContain("Open Identity", frontendAccessControlHtml);

        var frontendIdentityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Identity.Value)}");
        Assert.Contains("Enable identity", frontendIdentityHtml);
        Assert.Contains("Identity not enabled", frontendIdentityHtml);
        Assert.Contains("Enable identity for this resource before provisioning identity or assigning access permissions.", frontendIdentityHtml);

        var frontendDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/details");
        Assert.Contains(">Identity<", frontendDetailsHtml);
        Assert.Contains("ASP.NET Core project / application-topology-frontend", frontendDetailsHtml);
        Assert.DoesNotContain("ASP.NET Core project / application:application-topology-frontend", frontendDetailsHtml);
        Assert.Contains("Access control", frontendDetailsHtml);
        Assert.Contains(">Dependency graph<", frontendDetailsHtml);
        Assert.Contains(">Depends on<", frontendDetailsHtml);
        Assert.Contains(">Used by<", frontendDetailsHtml);
        Assert.Contains("application-topology-api", frontendDetailsHtml);
        Assert.Contains("application.aspnet-core-project", frontendDetailsHtml);
        Assert.Contains("Health:", frontendDetailsHtml);
        Assert.Contains("app.application-topology.cloudshell.local", frontendDetailsHtml);
        Assert.Contains("app.application-topology.cloudshell.local -&gt; application-topology-frontend/http", frontendDetailsHtml);
        Assert.Contains("Zone: Local DNS", frontendDetailsHtml);
        Assert.Contains("Provider: local-hostnames", frontendDetailsHtml);
        Assert.Contains("Materialization: provider selected", frontendDetailsHtml);

        var frontendDetailsRouteHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}");
        Assert.Contains(">Identity<", frontendDetailsRouteHtml);
        Assert.Contains("application-topology-api", frontendDetailsRouteHtml);

        var frontendOverviewRouteHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/overview");
        Assert.Contains(">Identity<", frontendOverviewRouteHtml);
        Assert.Contains("application-topology-api", frontendOverviewRouteHtml);

        var dnsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("dns:application-topology-local")}/details");
        Assert.Contains("app.application-topology.cloudshell.local", dnsDetailsHtml);
        Assert.Contains("application-topology-frontend/http", dnsDetailsHtml);
        Assert.DoesNotContain("application:application-topology-frontend/http", dnsDetailsHtml);
        Assert.Contains("local-hostnames", dnsDetailsHtml);

        var sqlEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Application exposure", sqlEndpointsHtml);
        Assert.Contains("type=cloudshell.loadBalancer", sqlEndpointsHtml);
        Assert.Contains("targetResourceId=application%3Aapplication-topology-sql-server", sqlEndpointsHtml);
        Assert.Contains("targetEndpointName=tds", sqlEndpointsHtml);
        Assert.Contains("routeKind=tcp", sqlEndpointsHtml);
        Assert.Contains("returnUrl=%2Fresources%2Fapplication%253Aapplication-topology-sql-server%2Fendpoints", sqlEndpointsHtml);

        var globalLogsHtml = await host.GetStringAsync("/logs");
        Assert.Contains("All resources", globalLogsHtml);
        Assert.Contains(">All logs<", globalLogsHtml);
        Assert.Contains("application-topology-api / Console logs", globalLogsHtml);
        Assert.Contains("application-topology-frontend / Console logs", globalLogsHtml);
        Assert.DoesNotContain(" / Activity", globalLogsHtml);

        var selectedLogHtml = await host.GetStringAsync(
            $"/logs?logId={Uri.EscapeDataString("application:application-topology-api:logs")}");
        Assert.Contains("All resources", selectedLogHtml);
        Assert.Contains("application-topology-api / Console logs", selectedLogHtml);
        Assert.Contains("application-topology-frontend / Console logs", selectedLogHtml);

        var apiLogsHtml = await host.GetStringAsync(
            $"/logs?resourceId={Uri.EscapeDataString("application:application-topology-api")}");
        Assert.Contains("All resources", apiLogsHtml);
        Assert.Contains("application-topology-api / Console logs", apiLogsHtml);
        Assert.DoesNotContain("application-topology-frontend / Console logs", apiLogsHtml);

        var missingLogHtml = await host.GetStringAsync(
            $"/logs?logId={Uri.EscapeDataString("application:application-topology-missing:logs")}");
        Assert.Contains("Log source not found", missingLogHtml);
        Assert.Contains("application:application-topology-missing:logs", missingLogHtml);
        Assert.Contains("Show available logs", missingLogHtml);

        var missingResourceLogFilterHtml = await host.GetStringAsync(
            $"/logs?resourceId={Uri.EscapeDataString("application:application-topology-missing")}");
        Assert.Contains("Resource log filter not found", missingResourceLogFilterHtml);
        Assert.Contains("application-topology-missing", missingResourceLogFilterHtml);
        Assert.Contains("Show all logs", missingResourceLogFilterHtml);

        var inlineApiLogsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}");
        Assert.Contains("application-topology-api / Console logs", inlineApiLogsHtml);
        Assert.DoesNotContain("All resources", inlineApiLogsHtml);
        Assert.DoesNotContain("Resource telemetry", inlineApiLogsHtml);
        Assert.DoesNotContain("application-topology-frontend / Console logs", inlineApiLogsHtml);

        var missingInlineLogHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&logId={Uri.EscapeDataString("application:application-topology-missing:logs")}");
        Assert.Contains("Log source not found", missingInlineLogHtml);
        Assert.Contains("application:application-topology-missing:logs", missingInlineLogHtml);
        Assert.Contains("Show available logs", missingInlineLogHtml);

        var missingResourceViewHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString("management:does-not-exist")}");
        Assert.Contains("Resource view not found", missingResourceViewHtml);
        Assert.Contains("management:does-not-exist", missingResourceViewHtml);
        Assert.Contains("Open overview", missingResourceViewHtml);

        var missingResourceHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:does-not-exist")}/details");
        Assert.Contains("Resource not found", missingResourceHtml);
        Assert.Contains("application:does-not-exist", missingResourceHtml);
        Assert.Contains("Open Resources", missingResourceHtml);

        var sqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details");
        Assert.Contains(">Identity<", sqlDetailsHtml);
        Assert.Contains("Access control", sqlDetailsHtml);
        Assert.Contains("SQL Server", sqlDetailsHtml);
        Assert.Contains("SQL Server / application-topology-sql-server", sqlDetailsHtml);
        Assert.DoesNotContain("SQL Server / application:application-topology-sql-server", sqlDetailsHtml);
        Assert.Contains("1 declared database", sqlDetailsHtml);
        Assert.Contains("Administrator", sqlDetailsHtml);
        Assert.Contains("Storage mounts", sqlDetailsHtml);
        Assert.Contains("SQL Data (FileSystem)", sqlDetailsHtml);
        Assert.Contains("/var/opt/mssql", sqlDetailsHtml);
        Assert.Contains("Read/write", sqlDetailsHtml);
        Assert.Contains("Database grants are recorded in CloudShell.", sqlDetailsHtml);
        Assert.Contains("Reconcile access to create SQL-side users and roles", sqlDetailsHtml);
        Assert.Contains("procedure-message warning", sqlDetailsHtml);
        Assert.DoesNotContain("<dt>Image</dt>", sqlDetailsHtml);
        Assert.DoesNotContain("<h3>Container host</h3>", sqlDetailsHtml);
        AssertResourceTabsInOrder(
            sqlDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Endpoints<",
            ">DNS<",
            ">Storage<",
            ">Databases<",
            ">Environment<",
            ">Identity<",
            ">Access control<",
            ">Activity<");
        Assert.DoesNotContain(">Deployment<", sqlDetailsHtml);
        Assert.DoesNotContain(">Scale and replicas<", sqlDetailsHtml);

        var databasesTabId = new ResourceViewId(ResourceTabGroupIds.Application, "databases");
        var sqlDatabasesHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(databasesTabId.Value)}");
        Assert.Contains("<th>Database</th>", sqlDatabasesHtml);
        Assert.Contains("<th>Type</th>", sqlDatabasesHtml);
        Assert.Contains("<th>State</th>", sqlDatabasesHtml);
        Assert.Contains("<th>Verification</th>", sqlDatabasesHtml);
        Assert.Contains("Application Topology", sqlDatabasesHtml);
        Assert.Contains("Declared database", sqlDatabasesHtml);
        Assert.Contains("application_topology", sqlDatabasesHtml);
        Assert.Contains("not reported", sqlDatabasesHtml);
        Assert.Contains("Existence not verified", sqlDatabasesHtml);
        Assert.Contains("Declared databases are shown until the SQL Server instance is running.", sqlDatabasesHtml);

        var sqlIdentityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Identity.Value)}");
        Assert.Contains("Enable identity", sqlIdentityHtml);
        Assert.Contains("Provisioned", sqlIdentityHtml);
        Assert.Contains("Built-in resource identity client is registered.", sqlIdentityHtml);
        Assert.Contains("application-topology-api / application-topology-api", sqlIdentityHtml);
        Assert.Contains(DatabaseResourceOperationPermissions.ReadWrite, sqlIdentityHtml);

        var sqlAccessControlHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.AccessControl.Value)}");
        Assert.Contains("Search principals", sqlAccessControlHtml);
        Assert.Contains("Assigned principals", sqlAccessControlHtml);
        Assert.Contains("Database grants are saved in CloudShell.", sqlAccessControlHtml);
        Assert.Contains("Reconcile access to create SQL-side users and roles", sqlAccessControlHtml);
        Assert.Contains("procedure-message warning", sqlAccessControlHtml);
        Assert.Contains("Database: read/write", sqlAccessControlHtml);
        Assert.Contains("Database access: reconcile", sqlAccessControlHtml);
        Assert.Contains("Effective access: pending", sqlAccessControlHtml);
        Assert.Contains("Start SQL Server to inspect or reconcile database users and roles.", sqlAccessControlHtml);
        Assert.Contains("application-topology-api", sqlAccessControlHtml);
        Assert.Contains(DatabaseResourceOperationPermissions.ReadWrite, sqlAccessControlHtml);
        Assert.Contains(DatabaseResourceOperationPermissions.ReconcileAccess, sqlAccessControlHtml);
        Assert.DoesNotContain("Deploy image", sqlAccessControlHtml);
    }

    [Fact]
    public void ApplicationTopologyFailureProblemExtensions_IncludeTraceResourceAndUpstreamStatus()
    {
        using var activity = new Activity("application-topology-failure").Start();

        var extensions = ApplicationTopologyProblemDetails.CreateFailureExtensions(
            "application-topology-frontend",
            upstreamStatusCode: 500);

        Assert.Equal("application-topology-frontend", extensions["resourceName"]);
        Assert.Equal("intentional", extensions["sampleFailureKind"]);
        Assert.Equal(500, extensions["upstreamStatusCode"]);
        Assert.Equal(activity.TraceId.ToHexString(), extensions["traceId"]);
    }

    [Fact]
    public async Task ApplicationTopologyHost_RuntimeFailurePathReturnsCorrelatedProblemDetails()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);
        await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Aapplication-topology-api/actions/start?startDependencies=false&ignoreDependentWarning=true");

        var startedApiJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var startedApiDocument = JsonDocument.Parse(startedApiJson);
        var api = Assert.Single(startedApiDocument.RootElement.EnumerateArray(), resource =>
            resource.GetProperty("id").GetString() == "application:application-topology-api");
        var apiEndpoint = GetPrimaryEndpointAddress(api);
        var apiFailureJson = await host.WaitForAbsoluteHttpStatusAsync(
            $"{apiEndpoint.TrimEnd('/')}/failure",
            HttpStatusCode.InternalServerError,
            StartupTimeout);
        using var apiFailureDocument = JsonDocument.Parse(apiFailureJson);
        Assert.Equal("Intentional sample failure", apiFailureDocument.RootElement.GetProperty("title").GetString());
        Assert.Equal("application-topology-api", apiFailureDocument.RootElement.GetProperty("resourceName").GetString());
        Assert.Equal("intentional", apiFailureDocument.RootElement.GetProperty("sampleFailureKind").GetString());
        Assert.Matches("^[0-9a-f]{32}$", apiFailureDocument.RootElement.GetProperty("traceId").GetString() ?? string.Empty);

        await host.SendAsync(
            HttpMethod.Post,
            "/api/control-plane/v1/resources/application%3Aapplication-topology-frontend/actions/start?startDependencies=false&ignoreDependentWarning=true");

        var frontendFailureJson = await host.WaitForAbsoluteHttpStatusAsync(
            $"http://localhost:{frontendPort}/upstream/failure",
            HttpStatusCode.BadGateway,
            StartupTimeout);
        using var frontendFailureDocument = JsonDocument.Parse(frontendFailureJson);
        Assert.Equal("Intentional upstream failure", frontendFailureDocument.RootElement.GetProperty("title").GetString());
        Assert.Equal("application-topology-frontend", frontendFailureDocument.RootElement.GetProperty("resourceName").GetString());
        Assert.Equal("intentional", frontendFailureDocument.RootElement.GetProperty("sampleFailureKind").GetString());
        Assert.Equal(500, frontendFailureDocument.RootElement.GetProperty("upstreamStatusCode").GetInt32());
        Assert.Matches("^[0-9a-f]{32}$", frontendFailureDocument.RootElement.GetProperty("traceId").GetString() ?? string.Empty);

        var fallbackJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
            $"http://localhost:{frontendPort}/upstream/fallback",
            StartupTimeout);
        using var fallbackDocument = JsonDocument.Parse(fallbackJson);
        var fallback = fallbackDocument.RootElement;
        var fallbackTraceId = fallback.GetProperty("traceId").GetString();
        Assert.Equal("Application Topology Frontend", fallback.GetProperty("frontend").GetString());
        Assert.Equal(500, fallback.GetProperty("fallback").GetProperty("failedAttemptStatusCode").GetInt32());
        Assert.True(fallback.GetProperty("fallback").GetProperty("recovered").GetBoolean());
        Assert.Equal("Hello from the referenced API project.", fallback.GetProperty("upstream").GetProperty("message").GetString());
        Assert.Matches("^[0-9a-f]{32}$", fallbackTraceId ?? string.Empty);

        var fallbackSpans = await WaitForTraceSpansAsync(
            host,
            fallbackTraceId!,
            StartupTimeout,
            spans =>
                spans.Any(span => IsHttpClientSpanForPath(span, "/failure", "Error")) &&
                spans.Any(span => IsHttpClientSpanForPath(span, "/message", "Unset")));
        Assert.Contains(
            fallbackSpans,
            span =>
                IsHttpClientSpanForPath(span, "/failure", "Error"));
        Assert.Contains(
            fallbackSpans,
            span => IsHttpClientSpanForPath(span, "/message", "Unset"));

        var fallbackTraceListHtml = await host.GetStringAsync(
            $"/observability/traces?resourceId={Uri.EscapeDataString("application:application-topology-frontend")}");
        Assert.Contains("GET /upstream/fallback", fallbackTraceListHtml);
        Assert.Contains("recent-trace-item attention", fallbackTraceListHtml);
        Assert.Contains("Needs attention: 1 error span(s)", fallbackTraceListHtml);

        var frontendMetrics = await WaitForMetricPointsAsync(
            host,
            "application:application-topology-frontend",
            StartupTimeout,
            points =>
                points.Any(point => IsHttpMetricForPath(point, "http.server.requests", "/upstream/fallback")) &&
                points.Any(point => IsHttpMetricForPath(point, "http.server.duration", "/upstream/fallback")));
        Assert.Contains(
            frontendMetrics,
            point => IsHttpMetricForPath(point, "http.server.requests", "/upstream/fallback"));
        Assert.Contains(
            frontendMetrics,
            point => IsHttpMetricForPath(point, "http.server.duration", "/upstream/fallback"));

        var frontendMetricsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-frontend")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}");
        Assert.Contains("Telemetry", frontendMetricsHtml);
        Assert.Contains("http.server.requests", frontendMetricsHtml);
        Assert.Contains("http.server.duration", frontendMetricsHtml);
        Assert.Contains("application-topology-frontend", frontendMetricsHtml);
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_SqlInclusiveRuntimePathConnectsFrontendApiAndDatabase()
    {
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);
            await host.SendAsync(
                HttpMethod.Post,
                "/api/control-plane/v1/resources/application%3Aapplication-topology-frontend/actions/start?startDependencies=true");

            var upstreamJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{frontendPort}/upstream",
                StartupTimeout);
            using var upstreamDocument = JsonDocument.Parse(upstreamJson);
            var upstream = upstreamDocument.RootElement;
            Assert.Equal("Application Topology Frontend", upstream.GetProperty("frontend").GetString());
            Assert.Equal("https+http://application-topology-api", upstream.GetProperty("logicalApiEndpoint").GetString());
            Assert.Equal("Hello from the referenced API project.", upstream.GetProperty("upstream").GetProperty("message").GetString());
            Assert.Equal("Development", upstream.GetProperty("settings").GetProperty("mode").GetString());
            Assert.True(upstream.GetProperty("settings").GetProperty("externalApiKeyConfigured").GetBoolean());
            Assert.Equal("ok", upstream.GetProperty("database").GetProperty("status").GetString());
            Assert.Equal("mssql", upstream.GetProperty("database").GetProperty("provider").GetString());
            Assert.Equal("application_topology", upstream.GetProperty("database").GetProperty("database").GetString());

            var eventsJson = await host.GetStringAsync(
                "/api/control-plane/v1/resource-events?resourceId=application%3Aapplication-topology-sql-server");
            using var eventsDocument = JsonDocument.Parse(eventsJson);
            var credentialEvent = Assert.Single(
                eventsDocument.RootElement.EnumerateArray(),
                resourceEvent => string.Equals(
                    resourceEvent.GetProperty("eventType").GetString(),
                    "event.provider.applications.sql-server.credential.resolved",
                    StringComparison.OrdinalIgnoreCase));
            Assert.Equal(
                "application:application-topology-sql-server",
                credentialEvent.GetProperty("resourceId").GetString());
            Assert.Equal("Information", credentialEvent.GetProperty("severity").GetString());
            Assert.Contains(
                "application_topology",
                credentialEvent.GetProperty("message").GetString() ?? string.Empty);
            Assert.DoesNotContain(
                "Password=",
                credentialEvent.GetProperty("message").GetString() ?? string.Empty,
                StringComparison.OrdinalIgnoreCase);

            var databasesTabId = new ResourceViewId(ResourceTabGroupIds.Application, "databases");
            var sqlDatabasesHtml = await host.GetStringAsync(
                $"/resources/{Uri.EscapeDataString("application:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(databasesTabId.Value)}");
            Assert.Contains("Application Topology", sqlDatabasesHtml);
            Assert.Contains("Declared database, exists on server", sqlDatabasesHtml);
            Assert.Contains("Existing system database", sqlDatabasesHtml);
            Assert.Contains("master", sqlDatabasesHtml);
            Assert.Contains("Verified from live SQL Server", sqlDatabasesHtml);

            var frontendFailureJson = await host.WaitForAbsoluteHttpStatusAsync(
                $"http://localhost:{frontendPort}/upstream/failure",
                HttpStatusCode.BadGateway,
                StartupTimeout);
            using var frontendFailureDocument = JsonDocument.Parse(frontendFailureJson);
            Assert.Equal("application-topology-frontend", frontendFailureDocument.RootElement.GetProperty("resourceName").GetString());
            Assert.Equal(500, frontendFailureDocument.RootElement.GetProperty("upstreamStatusCode").GetInt32());
            Assert.Matches("^[0-9a-f]{32}$", frontendFailureDocument.RootElement.GetProperty("traceId").GetString() ?? string.Empty);
        }
        finally
        {
            await StopResourceIfRunningAsync(host, "application:application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application:application-topology-api");
            await StopResourceIfRunningAsync(host, "application:application-topology-sql-server");
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_GracefulShutdownRemovesSqlServerContainer()
    {
        const string sqlContainerName = "cloudshell-application-application-topology-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage) ||
            await DockerComposeStack.ContainerExistsAsync(sqlContainerName))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        SampleProcess? host = null;
        var shouldCleanupContainer = false;

        try
        {
            host = await SampleProcess.StartAsync(
                "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
                await GetFreePortAsync(),
                [
                    ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                    ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                    ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                    ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
                ]);

            await host.WaitForHttpOkAsync("/", StartupTimeout);
            shouldCleanupContainer = true;
            await host.SendAsync(
                HttpMethod.Post,
                "/api/control-plane/v1/resources/application%3Aapplication-topology-sql-server/actions/start");
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");

            await host.StopAsync(TimeSpan.FromSeconds(30));

            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed during graceful host shutdown.");
            shouldCleanupContainer = false;
        }
        finally
        {
            host?.Dispose();
            if (shouldCleanupContainer)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
            }
        }
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
            "/api/control-plane/v1/resource-permission-grants" +
            $"?principalKind={(int)ResourcePrincipalKind.ResourceIdentity}" +
            $"&principalId={Uri.EscapeDataString("application:settings-secrets-api/identities/settings-secrets-api")}");
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
            $"/resources/{Uri.EscapeDataString("configuration:sample-app")}/details?tab={Uri.EscapeDataString("general:entries")}");
        AssertResourceTabsInOrder(
            settingsDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Entries<",
            ">Endpoints<");
        Assert.Contains("Configuration entries", settingsDetailsHtml);
        Assert.Contains("2 entries", settingsDetailsHtml);
        Assert.DoesNotContain(">Settings<", settingsDetailsHtml);
        Assert.DoesNotContain("aria-label=\"Entries\"", settingsDetailsHtml);

        var secretsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets-vault:sample-app")}/details?tab={Uri.EscapeDataString("general:secrets")}");
        AssertResourceTabsInOrder(
            secretsDetailsHtml,
            ">Overview<",
            ">Configuration<",
            ">Secrets<",
            ">Endpoints<");
        Assert.Contains("Vault secrets", secretsDetailsHtml);
        Assert.Contains("1 secret", secretsDetailsHtml);
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
            "https+http://configuration-sample-app",
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
    [Trait("Category", "DockerIntegration")]
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
            await GetFreePortAsync(),
            [
                ("Authentication__Enabled", "false")
            ]);

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

        var sampleDatabaseGrantsJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants?targetResourceId=sample%3Adatabase");
        using var sampleDatabaseGrantsDocument = JsonDocument.Parse(sampleDatabaseGrantsJson);
        Assert.Contains(
            sampleDatabaseGrantsDocument.RootElement.EnumerateArray(),
            grant =>
            {
                var grantPrincipal = grant.GetProperty("principal");
                return grantPrincipal.GetProperty("kind").GetInt32() == (int)ResourcePrincipalKind.User &&
                    grantPrincipal.GetProperty("id").GetString() == "alice" &&
                    grant.GetProperty("permission").GetString() == CloudShellPermissions.Resources.Manage;
            });

        var activityHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("sample:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Activity.Value)}");
        Assert.Contains("Activity", activityHtml);
        Assert.Contains("Event type", activityHtml);
        Assert.Contains("Triggered by", activityHtml);
        Assert.Contains("Triggered by user", activityHtml);
        Assert.Contains("Time range", activityHtml);
        Assert.Contains("Lifecycle actions", activityHtml);
        Assert.Contains("Lifecycle events", activityHtml);
        Assert.Contains("action.lifecycle.stop", activityHtml);
        Assert.Contains("event.lifecycle.stopped", activityHtml);
        Assert.Contains("Stop completed", activityHtml);
    }

    [Fact]
    public async Task ResourceHostSample_InMemoryIdentityUserCanLoginAndAccessGrantedResource()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/CloudShell.ResourceHost/CloudShell.ResourceHost.csproj",
            await GetFreePortAsync(),
            [
                ("Authentication__Enabled", "true"),
                ("Authentication__Mode", "Identity"),
                ("Authentication__AllowLocalSetup", "true")
            ]);

        await host.WaitForHttpOkAsync("/account/login", StartupTimeout);

        var cookies = new CookieContainer();
        using var handler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false
        };
        using var client = new HttpClient(handler)
        {
            BaseAddress = host.BaseAddress,
            Timeout = StartupTimeout
        };
        var loginHtml = await client.GetStringAsync("/account/login");
        Assert.Contains("Sign in to CloudShell", loginHtml);
        Assert.Contains("data-cloudshell-language-select", loginHtml);
        Assert.Contains("data-cloudshell-theme-select", loginHtml);
        Assert.Contains("name=\"Input.Identifier\"", loginHtml);
        Assert.Contains("name=\"Input.Credential\"", loginHtml);

        var loginToken = ExtractRequestVerificationToken(loginHtml);
        using var userNameLoginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "login",
                ["__RequestVerificationToken"] = loginToken,
                ["Input.Identifier"] = "alice",
                ["Input.Credential"] = "CloudShell123!"
            }));
        Assert.Equal(HttpStatusCode.OK, userNameLoginResponse.StatusCode);
        var userNameLoginResponseHtml = await userNameLoginResponse.Content.ReadAsStringAsync();
        Assert.Contains("The email or password is invalid.", userNameLoginResponseHtml);

        loginHtml = await client.GetStringAsync("/account/login");
        loginToken = ExtractRequestVerificationToken(loginHtml);
        using var loginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "login",
                ["__RequestVerificationToken"] = loginToken,
                ["Input.Identifier"] = "alice@example.test",
                ["Input.Credential"] = "CloudShell123!"
            }));
        Assert.Equal(HttpStatusCode.Redirect, loginResponse.StatusCode);

        var dashboardHtml = await client.GetStringAsync("/");
        Assert.Contains("href=\"/account/logout\"", dashboardHtml);
        Assert.Contains("data-enhance-nav=\"false\"", dashboardHtml);

        var resourcesJson = await client.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resourceIds = resourcesDocument.RootElement
            .EnumerateArray()
            .Select(resource => resource.GetProperty("id").GetString())
            .OfType<string>()
            .ToArray();
        Assert.Equal(["sample:database"], resourceIds);

        using var apiResponse = await client.GetAsync(
            "/api/control-plane/v1/resources/sample%3Aapi");
        Assert.Equal(HttpStatusCode.NotFound, apiResponse.StatusCode);

        var databaseJson = await client.GetStringAsync(
            "/api/control-plane/v1/resources/sample%3Adatabase");
        using var databaseDocument = JsonDocument.Parse(databaseJson);
        Assert.Equal("sample:database", databaseDocument.RootElement.GetProperty("id").GetString());
        var stopHref = databaseDocument.RootElement
            .GetProperty("resourceActions")
            .GetProperty("stop")
            .GetProperty("href")
            .GetString() ?? throw new InvalidOperationException("The database stop action did not include an href.");
        using var stopResponse = await client.PostAsync(stopHref, null);
        Assert.Equal(HttpStatusCode.OK, stopResponse.StatusCode);

        var eventsJson = await client.GetStringAsync(
            "/api/control-plane/v1/resource-events?resourceId=sample%3Adatabase&triggeredBy=alice%40example.test");
        using var eventsDocument = JsonDocument.Parse(eventsJson);
        Assert.Contains(
            eventsDocument.RootElement.EnumerateArray(),
            resourceEvent =>
                resourceEvent.GetProperty("eventType").GetString() ==
                    ResourceEventTypes.Actions.ForAction(ResourceActionIds.Stop) &&
                resourceEvent.GetProperty("triggeredBy").GetString() == "alice@example.test");

        var principalsJson = await client.GetStringAsync(
            "/api/control-plane/v1/resource-principals?kinds=User&searchText=alice");
        using var principalsDocument = JsonDocument.Parse(principalsJson);
        var principal = Assert.Single(principalsDocument.RootElement.EnumerateArray());
        Assert.Equal("Alice Local Developer", principal.GetProperty("displayName").GetString());
        Assert.Equal("alice", principal.GetProperty("reference").GetProperty("id").GetString());

        var grantsJson = await client.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants?targetResourceId=sample%3Adatabase");
        using var grantsDocument = JsonDocument.Parse(grantsJson);
        Assert.Contains(
            grantsDocument.RootElement.EnumerateArray(),
            grant =>
                grant.GetProperty("principal").GetProperty("id").GetString() == "alice" &&
                grant.GetProperty("permission").GetString() == CloudShellPermissions.Resources.Manage);

        var databaseAccessControlHtml = await client.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("sample:database")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.AccessControl.Value)}");
        Assert.Contains("Search principals", databaseAccessControlHtml);
        Assert.Contains("Assigned principals", databaseAccessControlHtml);
        Assert.Contains("Alice Local Developer", databaseAccessControlHtml);
        Assert.Contains("User", databaseAccessControlHtml);
        Assert.Contains(CloudShellPermissions.Resources.Manage, databaseAccessControlHtml);
        Assert.Contains("Revoke", databaseAccessControlHtml);
        Assert.DoesNotContain("sample:api", databaseAccessControlHtml);

        var logoutHtml = await client.GetStringAsync("/account/logout");
        var logoutToken = ExtractRequestVerificationToken(logoutHtml);
        using var logoutResponse = await client.PostAsync(
            "/account/logout",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "logout",
                ["__RequestVerificationToken"] = logoutToken
            }));
        Assert.Equal(HttpStatusCode.Redirect, logoutResponse.StatusCode);
        Assert.Equal("/account/login", logoutResponse.Headers.Location?.AbsolutePath);

        using var signedOutResourcesResponse = await client.GetAsync("/api/control-plane/v1/resources");
        Assert.Equal(HttpStatusCode.Redirect, signedOutResourcesResponse.StatusCode);
        Assert.Contains(
            "/account/login",
            signedOutResourcesResponse.Headers.Location?.OriginalString ?? string.Empty);

        var failedLoginHtml = await client.GetStringAsync("/account/login");
        var failedLoginToken = ExtractRequestVerificationToken(failedLoginHtml);
        using var failedLoginResponse = await client.PostAsync(
            "/account/login",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["_handler"] = "login",
                ["__RequestVerificationToken"] = failedLoginToken,
                ["Input.Identifier"] = "alice@example.test",
                ["Input.Credential"] = "WrongPassword123!"
            }));
        Assert.Equal(HttpStatusCode.OK, failedLoginResponse.StatusCode);
        var failedLoginResponseHtml = await failedLoginResponse.Content.ReadAsStringAsync();
        Assert.Contains("The email or password is invalid.", failedLoginResponseHtml);
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
        var appAttributes = app.GetProperty("attributes");
        var registryAttributes = registry.GetProperty("attributes");
        Assert.Equal(registryAddress, appAttributes.GetProperty("container.registry").GetString());
        Assert.Equal(registryAddress, registryAttributes.GetProperty("container.registry").GetString());
        Assert.Equal(
            ResourceDeclarationPersistence.Persisted.ToString(),
            appAttributes.GetProperty(ResourceAttributeNames.DeclarationPersistence).GetString());
        Assert.Equal(
            ResourceDeclarationPersistence.Persisted.ToString(),
            registryAttributes.GetProperty(ResourceAttributeNames.DeclarationPersistence).GetString());

        var appDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:sample-api")}/details");
        Assert.Contains("Persisted declaration", appDetailsHtml);
        Assert.DoesNotContain("Startup declaration", appDetailsHtml);
        Assert.DoesNotContain("UI changes are temporary until the resource is persisted.", appDetailsHtml);

        var appMonitoringHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:sample-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value)}");
        Assert.Contains("Auto-refresh", appMonitoringHtml);
        Assert.Contains(">Monitoring<", appMonitoringHtml);
        Assert.DoesNotContain(">Refresh<", appMonitoringHtml);

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
    public async Task ReplicatedContainerHealthSample_ProjectsReplicaHealthIntoParentAssessment()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync());

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        var appAttributes = app.GetProperty("attributes");

        Assert.Equal("true", appAttributes.GetProperty(ResourceAttributeNames.ContainerReplicasEnabled).GetString());
        Assert.Equal("3", appAttributes.GetProperty(ResourceAttributeNames.ContainerReplicas).GetString());
        Assert.Equal("3", appAttributes.GetProperty(ResourceAttributeNames.DeploymentProjectedReplicas).GetString());

        var childrenJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application:api")}/children");
        using var childrenDocument = JsonDocument.Parse(childrenJson);
        var replicas = childrenDocument.RootElement.EnumerateArray()
            .Where(resource =>
                resource.GetProperty("attributes").TryGetProperty(ResourceAttributeNames.RuntimeKind, out var kind) &&
                kind.GetString() == "containerReplica")
            .OrderBy(resource =>
                resource.GetProperty("attributes").GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString())
            .ToArray();

        Assert.Equal(3, replicas.Length);
        Assert.All(replicas, replica =>
        {
            Assert.Equal("application:api", replica.GetProperty("parentResourceId").GetString());
            Assert.Equal("application:api", replica.GetProperty("ownerResourceId").GetString());
            Assert.Equal("runtime.container", replica.GetProperty("typeId").GetString());
            Assert.Equal((int)ResourceManagementMode.RuntimeManaged, replica.GetProperty("managementMode").GetInt32());
            Assert.Equal((int)ResourceVisibility.Hidden, replica.GetProperty("visibility").GetInt32());
        });

        var summaryJson = await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application:api")}/health/refresh");
        using var summaryDocument = JsonDocument.Parse(summaryJson);
        var summary = summaryDocument.RootElement;
        var checks = summary.GetProperty("checks").EnumerateArray().ToArray();

        Assert.Equal("application:api", summary.GetProperty("resourceId").GetString());
        Assert.Equal((int)ResourceHealthStatus.Unknown, summary.GetProperty("status").GetInt32());
        Assert.Contains(checks, check =>
            check.GetProperty("check").GetProperty("type").GetInt32() == (int)ResourceProbeType.Health &&
            check.GetProperty("check").GetProperty("name").GetString() == "health");
        Assert.Contains(checks, check =>
            check.GetProperty("check").GetProperty("type").GetInt32() == (int)ResourceProbeType.Liveness &&
            check.GetProperty("check").GetProperty("name").GetString() == "alive");

        var liveness = Assert.Single(checks, check =>
            check.GetProperty("check").GetProperty("type").GetInt32() == (int)ResourceProbeType.Liveness);
        var observations = liveness.GetProperty("observations").EnumerateArray().ToArray();
        Assert.Equal(3, observations.Length);
        Assert.All(observations, observation =>
        {
            Assert.Equal("runtime", observation.GetProperty("scopeKind").GetString());
            Assert.Equal("alive", observation.GetProperty("attributes").GetProperty("health.check.name").GetString());
            Assert.Equal(ResourceProbeType.Liveness.ToString(), observation.GetProperty("attributes").GetProperty("health.check.type").GetString());
            Assert.StartsWith(
                "runtime-container:application-api:replica-",
                observation.GetProperty("resourceId").GetString());
        });

        var healthHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Health.Value)}");
        Assert.Contains("Health summary", healthHtml);
        Assert.Contains("runtime scope check(s)", healthHtml);

        var globalHealthHtml = await host.GetStringAsync("/health");
        Assert.Contains("api", globalHealthHtml);
        Assert.Contains("runtime scope check(s)", globalHealthHtml);

        var scalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        Assert.Contains("Health</th>", scalingHtml);
        Assert.Contains("health: No matching HTTP endpoint", scalingHtml);
        Assert.Contains("2 check(s): 0 healthy, 2 unknown, 0 unhealthy", scalingHtml);
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

        var apiEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Add route to load balancer", apiEndpointsHtml);
        Assert.Contains("/resources/load-balancer%3Apublic", apiEndpointsHtml);
        Assert.Contains("/configuration", apiEndpointsHtml);
        Assert.Contains("targetResourceId=application%3Aapi", apiEndpointsHtml);
        Assert.Contains("targetEndpointName=http", apiEndpointsHtml);
        Assert.DoesNotContain("type=cloudshell.loadBalancer", apiEndpointsHtml);

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

    private static string ExtractRequestVerificationToken(string html)
    {
        const string marker = "name=\"__RequestVerificationToken\" value=\"";
        var start = html.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
        {
            throw new InvalidOperationException("The response did not include a request verification token.");
        }

        start += marker.Length;
        var end = html.IndexOf('"', start);
        if (end < 0)
        {
            throw new InvalidOperationException("The request verification token was not closed.");
        }

        return WebUtility.HtmlDecode(html[start..end]);
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

    private static async Task AssertProvisionedIdentityStatusAsync(
        SampleProcess host,
        string resourceId)
    {
        var provisioning = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/identity/provisioning-status");
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
    }

    private static async Task<IReadOnlyList<JsonElement>> WaitForTraceSpansAsync(
        SampleProcess host,
        string traceId,
        TimeSpan timeout,
        Func<IReadOnlyList<JsonElement>, bool> isComplete)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;
        string? lastBody = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                lastBody = await host.GetStringAsync(
                    $"/api/control-plane/v1/traces?traceId={Uri.EscapeDataString(traceId)}&maxSpans=50");
                using var document = JsonDocument.Parse(lastBody);
                var spans = document.RootElement.EnumerateArray()
                    .Select(span => span.Clone())
                    .ToArray();
                if (spans.Length > 0 && isComplete(spans))
                {
                    return spans;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                lastException = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Trace '{traceId}' was not ingested within {timeout}." +
            $"{Environment.NewLine}{lastBody ?? lastException?.Message}");
    }

    private static bool IsHttpClientSpanForPath(
        JsonElement span,
        string path,
        string status)
    {
        if (!string.Equals(span.GetProperty("kind").GetString(), "Client", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(span.GetProperty("status").GetString(), status, StringComparison.OrdinalIgnoreCase) ||
            !span.TryGetProperty("spanAttributes", out var attributes) ||
            !attributes.TryGetProperty("url.full", out var url))
        {
            return false;
        }

        return url.GetString()?.EndsWith(path, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task<IReadOnlyList<JsonElement>> WaitForMetricPointsAsync(
        SampleProcess host,
        string resourceId,
        TimeSpan timeout,
        Func<IReadOnlyList<JsonElement>, bool> isComplete)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        Exception? lastException = null;
        string? lastBody = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            try
            {
                lastBody = await host.GetStringAsync(
                    $"/api/control-plane/v1/metrics?resourceId={Uri.EscapeDataString(resourceId)}&maxPoints=50");
                using var document = JsonDocument.Parse(lastBody);
                var points = document.RootElement.EnumerateArray()
                    .Select(point => point.Clone())
                    .ToArray();
                if (points.Length > 0 && isComplete(points))
                {
                    return points;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or JsonException)
            {
                lastException = exception;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Metrics for resource '{resourceId}' were not ingested within {timeout}." +
            $"{Environment.NewLine}{lastBody ?? lastException?.Message}");
    }

    private static async Task<bool> WaitForDockerContainerExistsAsync(string containerName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await DockerComposeStack.ContainerExistsAsync(containerName))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static async Task<bool> WaitForDockerContainerRemovedAsync(string containerName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await DockerComposeStack.ContainerExistsAsync(containerName))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static bool IsHttpMetricForPath(
        JsonElement point,
        string name,
        string path)
    {
        if (!string.Equals(point.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase) ||
            !point.TryGetProperty("attributes", out var attributes) ||
            !attributes.TryGetProperty("http.route", out var route))
        {
            return false;
        }

        return route.GetString()?.Contains(path, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static async Task StopResourceIfRunningAsync(
        SampleProcess host,
        string resourceId)
    {
        try
        {
            await host.SendAsync(
                HttpMethod.Post,
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/actions/stop?ignoreDependentWarning=true");
        }
        catch
        {
            // Cleanup should not hide the original test failure.
        }
    }

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

        var tabListEnd = html.IndexOf("cloudshell-tabbed-host registration-host", tabListStart, StringComparison.Ordinal);
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

        public static async Task<bool> IsImageAvailableAsync(string image)
        {
            try
            {
                var result = await RunDockerAsync(
                    SampleProcess.FindRepositoryRoot(),
                    ["image", "inspect", image],
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

        public static async Task<bool> ContainerExistsAsync(string containerName)
        {
            try
            {
                var result = await RunDockerAsync(
                    SampleProcess.FindRepositoryRoot(),
                    ["container", "inspect", containerName],
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

        public static async Task RemoveContainerIfExistsAsync(string containerName)
        {
            try
            {
                await RunDockerAsync(
                    SampleProcess.FindRepositoryRoot(),
                    ["rm", "-f", containerName],
                    null,
                    TimeSpan.FromSeconds(30),
                    throwOnError: false);
            }
            catch
            {
                // Test cleanup should not hide the original test failure.
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

        public async Task<string> WaitForAbsoluteHttpOkAndGetStringAsync(
            string url,
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
                    using var response = await client.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.IsSuccessStatusCode)
                    {
                        return body;
                    }

                    lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";
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

        public async Task<string> WaitForAbsoluteHttpStatusAsync(
            string url,
            HttpStatusCode expectedStatusCode,
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
                        $"Sample process exited with code {process.ExitCode} before '{url}' returned {(int)expectedStatusCode}.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    using var response = await client.GetAsync(url);
                    var body = await response.Content.ReadAsStringAsync();
                    if (response.StatusCode == expectedStatusCode)
                    {
                        return body;
                    }

                    lastStatus = $"{(int)response.StatusCode} {response.ReasonPhrase}: {body}";
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample process did not return {(int)expectedStatusCode} for '{url}' within {timeout}." +
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

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException(
                    $"{method} {path} failed before a response was received.{Environment.NewLine}{GetOutput()}",
                    exception);
            }

            using (response)
            {
                var body = await response.Content.ReadAsStringAsync();
                Assert.True(
                    response.IsSuccessStatusCode,
                    $"{method} {path} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
                return body;
            }
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

        public async Task StopAsync(TimeSpan timeout)
        {
            if (process.HasExited)
            {
                return;
            }

            await RequestGracefulShutdownAsync();
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
        }

        private async Task RequestGracefulShutdownAsync()
        {
            if (OperatingSystem.IsWindows())
            {
                if (process.CloseMainWindow())
                {
                    return;
                }

                process.Kill(entireProcessTree: true);
                return;
            }

            using var signal = Process.Start(new ProcessStartInfo("kill")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                ArgumentList =
                {
                    "-TERM",
                    process.Id.ToString(CultureInfo.InvariantCulture)
                }
            }) ?? throw new InvalidOperationException("Could not signal sample process shutdown.");
            await signal.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            if (signal.ExitCode != 0 && !process.HasExited)
            {
                throw new InvalidOperationException($"Could not signal sample process shutdown.{Environment.NewLine}{GetOutput()}");
            }
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
