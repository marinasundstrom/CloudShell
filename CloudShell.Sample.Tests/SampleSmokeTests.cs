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
using CloudShell.ApplicationTopologyHost;
using CloudShell.ApplicationTopology.ServiceDefaults;
using CloudShell.ContainerHost;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.Providers.Applications;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ReferenceProviders.ResourceManager;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using ResourceAttributeId = CloudShell.ResourceDefinitions.ResourceAttributeId;
using ResourceAttributeValue = CloudShell.ResourceDefinitions.ResourceAttributeValue;
using ResourceCapabilityId = CloudShell.ResourceDefinitions.ResourceCapabilityId;
using ResourceDefinitionJson = CloudShell.ResourceDefinitions.ResourceDefinitionJson;
using ResourceGraphState = CloudShell.ResourceDefinitions.ResourceState;
using ResourceHealthCheckCapabilityIds = CloudShell.ResourceDefinitions.ResourceHealthCheckCapabilityIds;
using ResourceReference = CloudShell.ResourceDefinitions.ResourceReference;
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
    private static readonly TimeSpan SampleHostLaunchTimeout = TimeSpan.FromMinutes(3);

    [Fact]
    public void SupportedSwitchReadinessSamples_DefaultToGraphOnly()
    {
        foreach (var sample in SupportedSwitchReadinessSampleGraphOnlySettings())
        {
            Assert.True(
                ReadBooleanConfigurationValue(sample.AppSettingsPath, sample.ConfigurationPath),
                $"{sample.AppSettingsPath} should set {sample.ConfigurationPath}=true.");
        }
    }

    private static IEnumerable<(string SampleName, string AppSettingsPath, string ConfigurationPath)> SupportedSwitchReadinessSampleGraphOnlySettings()
    {
        yield return ("ApplicationTopology", "samples/ApplicationTopology/Host/appsettings.json", "ApplicationTopology:GraphOnly");
        yield return ("ReplicatedContainerHealth", "samples/ReplicatedContainerHealth/appsettings.json", "ReplicatedContainerHealth:GraphOnly");
    }

    public static IEnumerable<object[]> SupportedSwitchReadinessSampleHostProjects()
    {
        var resourceHostPaths = new[] { "/", "/resources", "/api/control-plane/v1/resources" };
        yield return new object[]
        {
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/CloudShell.ContainerHost/CloudShell.ContainerHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/LoadBalancer/CloudShell.LoadBalancer.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/SplitHosting/ControlPlane/CloudShell.SplitHosting.ControlPlane.csproj",
            new[] { "/openapi/control-plane-v1.json", "/api/control-plane/v1/resources" }
        };
        yield return new object[]
        {
            "samples/ThirdPartyIdentity/CloudShell.ThirdPartyIdentity.csproj",
            resourceHostPaths
        };
    }

    [Theory]
    [MemberData(nameof(SupportedSwitchReadinessSampleHostProjects))]
    public async Task SupportedSwitchReadinessSampleHosts_StartAndRenderResources(
        string projectPath,
        string[] readinessPaths)
    {
        var port = await GetFreePortAsync();
        await CleanupSwitchReadinessRuntimeArtifactsAsync(projectPath);
        var host = await SampleProcess.StartAsync(
            projectPath,
            port,
            await CreateSampleHostLaunchEnvironmentAsync(projectPath, port));

        try
        {
            var isSplitControlPlane = projectPath.Contains(
                "/SplitHosting/ControlPlane/",
                StringComparison.OrdinalIgnoreCase);
            foreach (var path in readinessPaths)
            {
                if (string.Equals(path, "/api/control-plane/v1/resources", StringComparison.Ordinal))
                {
                    var token = isSplitControlPlane
                        ? await host.GetClientCredentialsTokenAsync(
                            "cloudshell-split-ui",
                            "local-development-client-secret",
                            "ControlPlane.Access")
                        : null;
                    using var resourcesDocument = JsonDocument.Parse(await host.GetStringAsync(path, token));
                    var resourceIds = resourcesDocument.RootElement
                        .EnumerateArray()
                        .Select(resource => resource.GetProperty("id").GetString())
                        .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
                        .Select(resourceId => resourceId!)
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);

                    Assert.NotEmpty(resourceIds);
                    AssertNoUnexpectedLegacyResources(projectPath, resourceIds);
                    continue;
                }

                await host.WaitForHttpOkAsync(path, SampleHostLaunchTimeout);
            }
        }
        finally
        {
            host.Dispose();
            await CleanupSwitchReadinessRuntimeArtifactsAsync(projectPath);
        }
    }

    [Fact]
    public void ContainerHostSample_ProjectsStorageBackedSqlServerResources()
    {
        const string storageResourceId = "cloudshell.storage:local";
        const string volumeResourceId = "cloudshell.volume:sql-data";
        const string sqlServerResourceId = "application.sql-server:sql-server";
        var contentRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var services = new ServiceCollection();
        services.AddSingleton<IHostEnvironment>(new TestHostEnvironment(contentRoot));
        services
            .AddInMemoryResourceModelGraph(
            [
                new ResourceGraphState(
                    "local",
                    StorageResourceTypeProvider.ResourceTypeId,
                    ResourceId: storageResourceId,
                    ProviderId: StorageResourceTypeProvider.ProviderId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        [StorageResourceTypeProvider.Attributes.Provider] = "local",
                        [StorageResourceTypeProvider.Attributes.Medium] = "FileSystem",
                        [StorageResourceTypeProvider.Attributes.Location] = "./Data/storage"
                    }),
                new ResourceGraphState(
                    "sql-data",
                    CloudShellVolumeResourceTypeProvider.ResourceTypeId,
                    ResourceId: volumeResourceId,
                    ProviderId: CloudShellVolumeResourceTypeProvider.ProviderId,
                    DisplayName: "SQL Server Data",
                    DependsOn:
                    [
                        ResourceReference.DependsOnResourceId(
                            storageResourceId,
                            typeId: StorageResourceTypeProvider.ResourceTypeId)
                    ],
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeValue>
                    {
                        [CloudShellVolumeResourceTypeProvider.Attributes.Provider] = "local",
                        [CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium] = "FileSystem",
                        [CloudShellVolumeResourceTypeProvider.Attributes.SubPath] = "sql-server",
                        [CloudShellVolumeResourceTypeProvider.Attributes.AccessMode] = "ReadWriteOnce",
                        [CloudShellVolumeResourceTypeProvider.Attributes.Persistent] = true
                    }),
                new ResourceGraphState(
                    "sql-server",
                    SqlServerResourceTypeProvider.ResourceTypeId,
                    ResourceId: sqlServerResourceId,
                    ProviderId: SqlServerResourceTypeProvider.ProviderId,
                    DisplayName: "SQL Server",
                    Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
                    {
                        [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                            ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                            [
                                new(volumeResourceId, "/var/opt/mssql")
                            ]))
                    })
            ])
            .AddStorageResourceType()
            .AddCloudShellVolumeResourceType()
            .AddSqlServerResourceType()
            .AddResourceModelGraphServices()
            .AddReferenceProviderResourceManagerProjections()
            .AddResourceModelGraphProcedureProvider(
                ResourceModelResourceProvider.DefaultProviderId,
                "Resource model");
        services.AddControlPlane();

        using var serviceProvider = services.BuildServiceProvider();
        var graphProvider = serviceProvider
            .GetServices<IResourceProvider>()
            .OfType<ResourceModelGraphProcedureProvider>()
            .Single();
        var resources = graphProvider
            .GetResources()
            .ToDictionary(resource => resource.Id, StringComparer.OrdinalIgnoreCase);
        var storage = resources[storageResourceId];
        Assert.Equal("cloudshell.storage", storage.EffectiveTypeId);
        Assert.Equal("local", storage.ResourceAttributes[StorageResourceTypeProvider.Attributes.Provider]);
        Assert.Equal("FileSystem", storage.ResourceAttributes[StorageResourceTypeProvider.Attributes.Medium]);
        Assert.Equal("./Data/storage", storage.ResourceAttributes[StorageResourceTypeProvider.Attributes.Location]);
        var volume = resources[volumeResourceId];
        Assert.Equal("cloudshell.volume", volume.EffectiveTypeId);
        Assert.Equal([storageResourceId], volume.DependsOn);
        Assert.Equal("FileSystem", volume.ResourceAttributes[
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium]);
        Assert.Equal("sql-server", volume.ResourceAttributes[
            CloudShellVolumeResourceTypeProvider.Attributes.SubPath]);
        var sqlServer = resources[sqlServerResourceId];
        Assert.Equal("application.sql-server", sqlServer.EffectiveTypeId);
        Assert.Contains(sqlServer.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ContainerHostSample_SqlRuntimeStartsWithStorageBackedVolume()
    {
        const string sqlServerResourceId = "application.sql-server:sql-server";
        var sqlContainerName = ContainerHostSqlServerDockerBridge.SqlServerContainerName;
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage) ||
            await DockerComposeStack.ContainerExistsAsync(sqlContainerName))
        {
            return;
        }

        var sqlPort = await GetFreePortAsync();
        var shouldCleanupSqlContainer = true;
        using var host = await SampleProcess.StartAsync(
            "samples/CloudShell.ContainerHost/CloudShell.ContainerHost.csproj",
            await GetFreePortAsync(),
            [
                ("ContainerHost__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
            var sqlServer = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() == sqlServerResourceId);

            Assert.DoesNotContain(resources, resource =>
                resource.GetProperty("id").GetString() == "storage:local");
            Assert.DoesNotContain(resources, resource =>
                resource.GetProperty("id").GetString() == "volume:sql-data");
            Assert.DoesNotContain(resources, resource =>
                resource.GetProperty("id").GetString() == "application:sql-server");
            Assert.Equal($"localhost:{sqlPort}", GetEndpointAddress(sqlServer, "tds"));
            await StartGraphResourceIfAvailableAsync(host, sqlServer, "ContainerHost SQL Server");
            await WaitForResourceStateAsync(
                host,
                sqlServerResourceId,
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");
            var sampleDataPath = Path.Combine(
                SampleProcess.FindRepositoryRoot(),
                "samples",
                "CloudShell.ContainerHost",
                "Data",
                "storage",
                "sql-server");
            Assert.True(
                Directory.Exists(sampleDataPath),
                $"Expected storage-backed volume path '{sampleDataPath}' to be created.");

            var startedSqlContainerId = await DockerComposeStack.GetContainerIdAsync(sqlContainerName) ??
                throw new InvalidOperationException(
                    $"Docker container '{sqlContainerName}' did not have an inspectable id.");
            await host.SendAsync(
                HttpMethod.Post,
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(sqlServerResourceId)}/actions/restart?ignoreDependentWarning=true");
            await WaitForResourceStateAsync(
                host,
                sqlServerResourceId,
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerIdChangedAsync(
                    sqlContainerName,
                    startedSqlContainerId,
                    StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be recreated after SQL restart.");

            await StopResourceIfRunningAsync(host, sqlServerResourceId);
            await WaitForResourceStateAsync(
                host,
                sqlServerResourceId,
                ResourceState.Stopped,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed after SQL stop.");
            shouldCleanupSqlContainer = false;
        }
        finally
        {
            await StopResourceIfRunningAsync(host, sqlServerResourceId);
            if (shouldCleanupSqlContainer)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
            }
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ContainerHostSample_SqlRuntimeStopsOnGracefulHostShutdown()
    {
        const string sqlServerResourceId = "application.sql-server:sql-server";
        var sqlContainerName = ContainerHostSqlServerDockerBridge.SqlServerContainerName;
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage))
        {
            return;
        }

        await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);

        var sqlPort = await GetFreePortAsync();
        var host = await SampleProcess.StartAsync(
            "samples/CloudShell.ContainerHost/CloudShell.ContainerHost.csproj",
            await GetFreePortAsync(),
            [
                ("ContainerHost__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var sqlServer = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == sqlServerResourceId);

            await StartGraphResourceIfAvailableAsync(host, sqlServer, "ContainerHost SQL Server");
            await WaitForResourceStateAsync(
                host,
                sqlServerResourceId,
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");

            await host.StopAsync(StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed during graceful host shutdown.");
        }
        finally
        {
            host.Dispose();
            await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
        }
    }

    [Fact]
    public async Task ProjectReferenceHost_RunsProjectsWithoutOldProviderRecords()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var apiEndpoint = $"http://127.0.0.1:{apiPort}";
        var frontendEndpoint = $"http://127.0.0.1:{frontendPort}";
        const string apiResourceId = "application.aspnet-core-project:project-reference-api";
        const string frontendResourceId = "application.aspnet-core-project:project-reference-frontend";
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync(),
            [
                ("ProjectReference__ApiEndpoint", apiEndpoint),
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

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == apiResourceId);
        var frontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == frontendResourceId);

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-api");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:project-reference-frontend");
        Assert.Equal(apiEndpoint, api.GetProperty("primaryEndpoint").GetString());
        Assert.Equal(frontendEndpoint, frontend.GetProperty("primaryEndpoint").GetString());

        await StartGraphResourceIfAvailableAsync(host, api, "ProjectReference API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{apiEndpoint}/health",
            bearerToken: null,
            StartupTimeout);
        var startedApi = await WaitForResourceStateAsync(
            host,
            apiResourceId,
            ResourceState.Running,
            StartupTimeout);
        Assert.True(HasResourceState(startedApi, ResourceState.Running));

        await StartGraphResourceIfAvailableAsync(host, frontend, "ProjectReference frontend");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{frontendEndpoint}/upstream",
            bearerToken: null,
            StartupTimeout);
        var upstreamJson = await host.GetAbsoluteStringAsync($"{frontendEndpoint}/upstream");
        using var upstreamDocument = JsonDocument.Parse(upstreamJson);
        Assert.StartsWith(
            apiEndpoint,
            upstreamDocument.RootElement.GetProperty("resolvedApiEndpoint").GetString(),
            StringComparison.Ordinal);
        Assert.Equal(
            "Hello from the referenced API project.",
            upstreamDocument.RootElement
                .GetProperty("upstream")
                .GetProperty("message")
                .GetString());

        var apiLogSourceId = await WaitForLogSourceAsync(host, apiResourceId);
        Assert.NotEmpty(await WaitForLogEntriesAsync(host, apiLogSourceId));
        var frontendLogSourceId = await WaitForLogSourceAsync(host, frontendResourceId);
        Assert.NotEmpty(await WaitForLogEntriesAsync(host, frontendLogSourceId));
        Assert.NotEmpty(await WaitForMetricPointsAsync(
            host,
            apiResourceId,
            StartupTimeout,
            points => points.Any(point =>
                point.GetProperty("name").GetString() == "http.server.requests" &&
                point.GetProperty("resourceId").GetString() == apiResourceId)));
        Assert.NotEmpty(await WaitForTraceSpansByResourceAsync(
            host,
            apiResourceId,
            StartupTimeout,
            spans => spans.Any(span =>
                span.GetProperty("name").GetString() == "api.prepare-message" &&
                span.GetProperty("resourceId").GetString() == apiResourceId)));
        Assert.NotEmpty(await WaitForTraceSpansByResourceAsync(
            host,
            frontendResourceId,
            StartupTimeout,
            spans => spans.Any(span =>
                span.GetProperty("name").GetString() == "frontend.call-project-reference-api" &&
                span.GetProperty("resourceId").GetString() == frontendResourceId)));

        var apiHealthJson = await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(apiResourceId)}/health/refresh");
        using var apiHealthDocument = JsonDocument.Parse(apiHealthJson);
        AssertGraphHealthRefreshSucceeded(apiHealthDocument.RootElement, apiResourceId);

        var frontendHealthJson = await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(frontendResourceId)}/health/refresh");
        using var frontendHealthDocument = JsonDocument.Parse(frontendHealthJson);
        AssertGraphHealthRefreshSucceeded(frontendHealthDocument.RootElement, frontendResourceId);

        var applyJson = await host.SendJsonAsync(
            HttpMethod.Post,
            $"/project-reference/resource-graph/resources/{Uri.EscapeDataString(apiResourceId)}/environment-variables",
            """
            {
              "name": "POC_UPDATE_MARKER",
              "value": "applied"
            }
            """);
        using var applyDocument = JsonDocument.Parse(applyJson);
        var apply = applyDocument.RootElement;
        Assert.True(apply.GetProperty("committed").GetBoolean());
        Assert.False(apply.GetProperty("hasErrors").GetBoolean());
        Assert.Equal("Committed", apply.GetProperty("status").GetString());
        Assert.True(apply.GetProperty("resultVersion").GetInt64() >
            apply.GetProperty("baseVersion").GetInt64());
        Assert.Contains(
            apply.GetProperty("diagnostics").EnumerateArray(),
            diagnostic =>
                diagnostic.GetProperty("severity").GetString() == "Warning" &&
                diagnostic.GetProperty("code").GetString() ==
                    "application.aspNetCoreProject.restartRequired");

        var apiDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(apiResourceId)}/details");
        Assert.Contains("Project Reference API", apiDetailsHtml);
        Assert.Contains("project-reference-api", apiDetailsHtml);

        var apiMetricsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(apiResourceId)}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}");
        Assert.Contains("Telemetry", apiMetricsHtml);
        Assert.Contains("http.server.requests", apiMetricsHtml);
        Assert.Contains("project-reference-api", apiMetricsHtml);

        await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(apiResourceId)}/actions/stop?ignoreDependentWarning=true");
        var stoppedApi = await WaitForResourceStateAsync(
            host,
            apiResourceId,
            ResourceState.Stopped,
            StartupTimeout);
        Assert.True(HasResourceState(stoppedApi, ResourceState.Stopped));
    }

    [Fact]
    public async Task ProjectReferenceHost_HonorsResourceManagerReadOnlySetting()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ProjectReference/Host/CloudShell.ProjectReferenceHost.csproj",
            await GetFreePortAsync(),
            [
                ("ResourceManager__ReadOnly", "true")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Resource Manager is in read-only mode", resourcesHtml);
        Assert.DoesNotContain(">Add resource<", resourcesHtml);
        Assert.DoesNotContain(">Create group<", resourcesHtml);

        var resourceDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.aspnet-core-project:project-reference-api")}/details");
        Assert.Contains("Stop unavailable. Resource Manager is in read-only mode.", resourceDetailsHtml);

        var addResourceHtml = await host.GetStringAsync("/resources/add");
        Assert.Contains("Resource registration is disabled", addResourceHtml);
        Assert.DoesNotContain("Create a resource group", addResourceHtml);
    }

    [Fact]
    public async Task ApplicationTopologyHost_ProjectsSqlStorageAndServiceDiscoveryTopology()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__GraphOnly", "false"),
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__GraphConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__GraphSecretsServiceEndpoint", graphSecretsEndpoint),
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
        var graphSqlServer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-server:graph-application-topology-sql-server");
        var graphDatabase = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-database:graph-application-topology-db");
        var graphSettings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:graph-application-topology-settings");
        var graphSecrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:graph-application-topology-secrets");
        var graphApi = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:graph-application-topology-api");
        var graphFrontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:graph-application-topology-frontend");
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
        var graphApiAttributes = graphApi.GetProperty("attributes");
        var graphFrontendAttributes = graphFrontend.GetProperty("attributes");
        var dnsZoneAttributes = dnsZone.GetProperty("attributes");
        var nameMappingAttributes = nameMapping.GetProperty("attributes");
        var settingsIdentity = settings.GetProperty("identity");
        var secretsIdentity = secrets.GetProperty("identity");
        var apiIdentity = api.GetProperty("identity");
        var graphApiIdentity = graphApi.GetProperty("identity");

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

        Assert.Equal("application.sql-server", graphSqlServer.GetProperty("typeId").GetString());
        Assert.Equal($"localhost:{sqlPort}", GetEndpointAddress(graphSqlServer, "tds"));
        Assert.Equal("application.sql-database", graphDatabase.GetProperty("typeId").GetString());
        Assert.Contains(
            "application.sql-server:graph-application-topology-sql-server",
            graphDatabase.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("configuration.store", graphSettings.GetProperty("typeId").GetString());
        Assert.Equal(graphConfigurationEndpoint +
            "/api/configuration/stores/configuration.store%3Agraph-application-topology-settings/entries",
            GetEndpointAddress(graphSettings, "entries"));
        Assert.Equal("secrets.vault", graphSecrets.GetProperty("typeId").GetString());
        Assert.Equal(graphSecretsEndpoint +
            "/api/secrets/vaults/secrets.vault%3Agraph-application-topology-secrets/secrets",
            GetEndpointAddress(graphSecrets, "secrets"));
        Assert.Equal("application.aspnet-core-project", graphApi.GetProperty("typeId").GetString());
        Assert.Equal($"http://localhost:{graphApiPort}", GetPrimaryEndpointAddress(graphApi));
        Assert.Equal("identity:development", graphApiIdentity.GetProperty("providerId").GetString());
        Assert.Equal("graph-application-topology-api", graphApiIdentity.GetProperty("name").GetString());
        Assert.Equal(
            "application-topology-api",
            graphApiAttributes
                .GetProperty(AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName.Value)
                .GetString());
        Assert.Contains(
            "application.sql-database:graph-application-topology-db",
            graphApi.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.DoesNotContain(
            "application.sql-server:graph-application-topology-sql-server",
            graphApi.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.EndsWith(
            "/samples/ApplicationTopology/Api/CloudShell.ApplicationTopologyApi.csproj",
            graphApiAttributes
                .GetProperty(AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath.Value)
                .GetString());
        Assert.Equal("application.aspnet-core-project", graphFrontend.GetProperty("typeId").GetString());
        Assert.Equal($"http://localhost:{graphFrontendPort}", GetPrimaryEndpointAddress(graphFrontend));
        Assert.Contains(
            "application.aspnet-core-project:graph-application-topology-api",
            graphFrontend.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.EndsWith(
            "/samples/ApplicationTopology/Frontend/CloudShell.ApplicationTopologyFrontend.csproj",
            graphFrontendAttributes
                .GetProperty(AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath.Value)
                .GetString());

        var resourcesHtml = await host.GetStringAsync("/resources");
        Assert.Contains("Graph Application Topology API", resourcesHtml);
        Assert.Contains("Graph Application Topology Frontend", resourcesHtml);
        Assert.Contains("Graph Application Topology SQL Server", resourcesHtml);
        Assert.Contains("Graph Application Topology Settings", resourcesHtml);
        Assert.Contains("Graph Application Topology Secrets", resourcesHtml);

        var graphApiDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-api")}/details");
        Assert.Contains("Graph Application Topology API", graphApiDetailsHtml);
        Assert.Contains("application.aspnet-core-project", graphApiDetailsHtml);
        Assert.Contains(">Identity<", graphApiDetailsHtml);
        Assert.Contains(">Relationships<", graphApiDetailsHtml);
        Assert.Contains(">Depends on<", graphApiDetailsHtml);
        Assert.Contains(">Used by<", graphApiDetailsHtml);
        Assert.Contains("graph-application-topology-db", graphApiDetailsHtml);
        Assert.Contains("graph-application-topology-settings", graphApiDetailsHtml);
        Assert.Contains("graph-application-topology-secrets", graphApiDetailsHtml);
        Assert.DoesNotContain("CloudShell-Passw0rd!", graphApiDetailsHtml);

        var graphApiEnvironmentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Environment.Value)}");
        Assert.Contains("Environment", graphApiEnvironmentHtml);
        Assert.DoesNotContain("CloudShell-Passw0rd!", graphApiEnvironmentHtml);

        var graphSqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:graph-application-topology-sql-server")}/details");
        Assert.Contains("Graph Application Topology SQL Server", graphSqlDetailsHtml);
        Assert.Contains("application.sql-server", graphSqlDetailsHtml);
        Assert.Contains("graph-application-topology-sql-data", graphSqlDetailsHtml);
        Assert.DoesNotContain("Deploy image", graphSqlDetailsHtml);

        var graphSettingsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("configuration.store:graph-application-topology-settings")}/details");
        Assert.Contains("Graph Application Topology Settings", graphSettingsDetailsHtml);
        Assert.Contains("configuration.store", graphSettingsDetailsHtml);
        Assert.Contains("2", graphSettingsDetailsHtml);

        var graphSecretsDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("secrets.vault:graph-application-topology-secrets")}/details");
        Assert.Contains("Graph Application Topology Secrets", graphSecretsDetailsHtml);
        Assert.Contains("secrets.vault", graphSecretsDetailsHtml);
        Assert.DoesNotContain("local-development-api-key", graphSecretsDetailsHtml);

        await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology settings");
        await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology secrets");
        await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{graphApiPort}/health",
            bearerToken: null,
            StartupTimeout);
        var graphApiSettingsJson = await host.GetAbsoluteStringAsync(
            $"http://localhost:{graphApiPort}/settings");
        using var graphApiSettingsDocument = JsonDocument.Parse(graphApiSettingsJson);
        var graphApiSettings = graphApiSettingsDocument.RootElement;
        Assert.Equal(
            "Hello from CloudShell graph configuration.",
            graphApiSettings.GetProperty("message").GetString());
        Assert.Equal("Graph", graphApiSettings.GetProperty("mode").GetString());
        Assert.True(graphApiSettings.GetProperty("externalApiKeyConfigured").GetBoolean());

        await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology Frontend");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{graphFrontendPort}/healthz",
            bearerToken: null,
            StartupTimeout);
        var graphFrontendFailureJson = await host.WaitForAbsoluteHttpStatusAsync(
            $"http://localhost:{graphFrontendPort}/upstream/failure",
            HttpStatusCode.BadGateway,
            StartupTimeout);
        Assert.Contains("Intentional upstream failure", graphFrontendFailureJson);
        Assert.Contains("Application Topology API failure endpoint returned 500", graphFrontendFailureJson);

        await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-frontend")}/actions/stop?ignoreDependentWarning=true");
        await host.SendAsync(
            HttpMethod.Post,
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-api")}/actions/stop?ignoreDependentWarning=true");

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
        var graphGrantsJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-permission-grants" +
            $"?principalKind={(int)ResourcePrincipalKind.ResourceIdentity}" +
            $"&principalId={Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-api/identities/graph-application-topology-api")}");
        using var graphGrantsDocument = JsonDocument.Parse(graphGrantsJson);
        var graphGrants = graphGrantsDocument.RootElement.EnumerateArray().ToArray();
        Assert.Contains(
            graphGrants,
            grant =>
                grant.GetProperty("targetResourceId").GetString() == "application.sql-server:graph-application-topology-sql-server" &&
                grant.GetProperty("permission").GetString() == DatabaseResourceOperationPermissions.ReadWrite);

        await AssertProvisionedIdentityStatusAsync(host, "application:application-topology-api");
        await AssertProvisionedIdentityStatusAsync(host, "application:application-topology-sql-server");
        await AssertProvisionedIdentityStatusAsync(host, "application.aspnet-core-project:graph-application-topology-api");
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
        Assert.Contains("Recent polling", healthHtml);

        var apiHealthHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Health.Value)}");
        Assert.Contains(">Health<", apiHealthHtml);
        Assert.Contains("Health summary", apiHealthHtml);
        Assert.Contains("Auto-refresh", apiHealthHtml);
        Assert.Contains("Recent polling", apiHealthHtml);
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
        Assert.Contains("ApplicationTopology__SqlServer__ResourceName", apiEnvironmentHtml);
        Assert.Contains("application-topology-sql-server", apiEnvironmentHtml);
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
            $"/logs?logSourceId={Uri.EscapeDataString("application:application-topology-api:logs")}");
        Assert.Contains("All resources", selectedLogHtml);
        Assert.Contains("application-topology-api / Console logs", selectedLogHtml);
        Assert.Contains("application-topology-frontend / Console logs", selectedLogHtml);

        var apiLogsHtml = await host.GetStringAsync(
            $"/logs?resourceId={Uri.EscapeDataString("application:application-topology-api")}");
        Assert.Contains("All resources", apiLogsHtml);
        Assert.Contains("application-topology-api / Console logs", apiLogsHtml);
        Assert.DoesNotContain("application-topology-frontend / Console logs", apiLogsHtml);

        var missingLogHtml = await host.GetStringAsync(
            $"/logs?logSourceId={Uri.EscapeDataString("application:application-topology-missing:logs")}");
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
            $"/resources/{Uri.EscapeDataString("application:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&logSourceId={Uri.EscapeDataString("application:application-topology-missing:logs")}");
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
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__GraphOnly", "false"),
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
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
    public async Task ApplicationTopologyHost_GraphBackingServicesRunThroughResourceModelRuntime()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__GraphConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__GraphSecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphSettings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:graph-application-topology-settings");
        var graphSecrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:graph-application-topology-secrets");
        var graphApi = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:graph-application-topology-api");

        var graphSettingsEndpoint = GetEndpointAddress(graphSettings, "entries");
        var graphSecretsEndpointAddress = GetEndpointAddress(graphSecrets, "secrets");
        Assert.StartsWith(
            graphConfigurationEndpoint,
            graphSettingsEndpoint,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"/api/configuration/stores/{Uri.EscapeDataString("configuration.store:graph-application-topology-settings")}/entries",
            graphSettingsEndpoint,
            StringComparison.Ordinal);
        Assert.StartsWith(
            graphSecretsEndpoint,
            graphSecretsEndpointAddress,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"/api/secrets/vaults/{Uri.EscapeDataString("secrets.vault:graph-application-topology-secrets")}/secrets",
            graphSecretsEndpointAddress,
            StringComparison.Ordinal);
        await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology graph settings");
        await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology graph secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphConfigurationEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphSecretsEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);

        var graphResourceToken = await host.GetClientCredentialsTokenAsync(
            "application.aspnet-core-project:graph-application-topology-api/graph-application-topology-api",
            "local-development-application-topology-api-secret",
            "ControlPlane.Access");
        var graphSettingsJson = await host.GetAbsoluteStringAsync(
            graphSettingsEndpoint,
            graphResourceToken);
        using var graphSettingsDocument = JsonDocument.Parse(graphSettingsJson);
        Assert.Contains(
            graphSettingsDocument.RootElement.EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "ApplicationTopology:Message" &&
                entry.GetProperty("value").GetString() == "Hello from CloudShell graph configuration.");
        Assert.Contains(
            graphSettingsDocument.RootElement.EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "ApplicationTopology:Mode" &&
                entry.GetProperty("value").GetString() == "Graph");

        var graphSecretJson = await host.GetAbsoluteStringAsync(
            $"{graphSecretsEndpointAddress.TrimEnd('/')}/ApplicationTopology--ExternalApiKey",
            graphResourceToken);
        using var graphSecretDocument = JsonDocument.Parse(graphSecretJson);
        Assert.Equal(
            "graph-local-development-api-key",
            graphSecretDocument.RootElement.GetProperty("value").GetString());

        await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology graph API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{graphApiPort}/health",
            bearerToken: null,
            StartupTimeout);
        var graphApiSettingsJson = await host.GetAbsoluteStringAsync(
            $"http://localhost:{graphApiPort}/settings");
        using var graphApiSettingsDocument = JsonDocument.Parse(graphApiSettingsJson);
        var graphApiSettings = graphApiSettingsDocument.RootElement;
        Assert.Equal("Hello from CloudShell graph configuration.", graphApiSettings.GetProperty("message").GetString());
        Assert.Equal("Graph", graphApiSettings.GetProperty("mode").GetString());
        Assert.True(graphApiSettings.GetProperty("externalApiKeyConfigured").GetBoolean());
    }

    [Fact]
    public async Task ApplicationTopologyHost_GraphOnlyModeDeclaresWorkloadThroughResourceModel()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__GraphConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__GraphSecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphStorage = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.storage:graph-application-topology-local");
        var graphVolume = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.volume:graph-application-topology-sql-data");
        var graphSqlServer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-server:graph-application-topology-sql-server");
        var graphDatabase = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-database:graph-application-topology-db");
        var graphSettings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:graph-application-topology-settings");
        var graphSecrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:graph-application-topology-secrets");
        var graphApi = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:graph-application-topology-api");
        var graphFrontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:graph-application-topology-frontend");
        var graphHostConfiguration = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.host:graph-application-topology-host-settings");
        var nameMapping = Assert.Single(resources, resource =>
            resource.GetProperty("typeId").GetString() == PlatformResourceProvider.NameMappingResourceType
            && resource.GetProperty("attributes")
                .GetProperty(ResourceAttributeNames.NameMappingHostName)
                .GetString() == "app.application-topology.cloudshell.local");

        foreach (var oldResourceId in new[]
        {
            "storage:application-topology-local",
            "volume:application-topology-sql-data",
            "application:application-topology-sql-server",
            "application:application-topology-sql-server/database:application-topology",
            "configuration:application-topology",
            "secrets-vault:application-topology",
            "application:application-topology-api",
            "application:application-topology-frontend"
        })
        {
            Assert.DoesNotContain(
                resources,
                resource => string.Equals(
                    resource.GetProperty("id").GetString(),
                    oldResourceId,
                    StringComparison.OrdinalIgnoreCase));
        }

        Assert.Equal(
            "application.aspnet-core-project:graph-application-topology-frontend",
            nameMapping.GetProperty("attributes")
                .GetProperty(ResourceAttributeNames.NameMappingTargetResourceId)
                .GetString());
        Assert.Equal("cloudshell.storage", graphStorage.GetProperty("typeId").GetString());
        Assert.Equal("cloudshell.volume", graphVolume.GetProperty("typeId").GetString());
        Assert.Equal("application.sql-server", graphSqlServer.GetProperty("typeId").GetString());
        Assert.Equal("application.sql-database", graphDatabase.GetProperty("typeId").GetString());
        Assert.Contains(
            "cloudshell.storage:graph-application-topology-local",
            graphVolume.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "cloudshell.volume:graph-application-topology-sql-data",
            graphSqlServer.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "application.sql-server:graph-application-topology-sql-server",
            graphDatabase.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal($"localhost:{sqlPort}", GetEndpointAddress(graphSqlServer, "tds"));
        Assert.Equal("configuration.host", graphHostConfiguration.GetProperty("typeId").GetString());
        Assert.Equal(
            "host",
            graphHostConfiguration.GetProperty("attributes").GetProperty("configuration.kind").GetString());
        Assert.Equal(
            "application-topology",
            graphHostConfiguration.GetProperty("attributes").GetProperty("configuration.source").GetString());
        Assert.Equal(
            "0",
            graphHostConfiguration.GetProperty("attributes").GetProperty("configuration.entries.count").GetString());
        Assert.True(
            graphHostConfiguration.GetProperty("resourceActions")
                .TryGetProperty(HostConfigurationSourceResourceTypeProvider.Operations.Inspect.ToString(), out _));

        var graphApiDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.aspnet-core-project:graph-application-topology-api")}/details");
        Assert.Contains("Graph Application Topology API", graphApiDetailsHtml);
        Assert.Contains("ASP.NET Core project", graphApiDetailsHtml);
        Assert.Contains("application.aspnet-core-project", graphApiDetailsHtml);

        var graphSqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:graph-application-topology-sql-server")}/details");
        Assert.Contains("Graph Application Topology SQL Server", graphSqlDetailsHtml);
        Assert.Contains("SQL Server", graphSqlDetailsHtml);
        Assert.Contains("application.sql-server", graphSqlDetailsHtml);

        var graphApplicationAddHtml = await host.GetStringAsync(
            "/resources/add?type=application.aspnet-core-project");
        Assert.Contains("Graph-backed application resources", graphApplicationAddHtml);
        Assert.DoesNotContain("Resource type not found", graphApplicationAddHtml);

        await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology graph-only settings");
        await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology graph-only secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphConfigurationEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphSecretsEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);

        await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology graph-only API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{graphApiPort}/health",
            bearerToken: null,
            StartupTimeout);
        var graphApiSettingsJson = await host.GetAbsoluteStringAsync(
            $"http://localhost:{graphApiPort}/settings");
        using var graphApiSettingsDocument = JsonDocument.Parse(graphApiSettingsJson);
        var graphApiSettings = graphApiSettingsDocument.RootElement;
        Assert.Equal("Hello from CloudShell graph configuration.", graphApiSettings.GetProperty("message").GetString());
        Assert.Equal("Graph", graphApiSettings.GetProperty("mode").GetString());
        Assert.True(graphApiSettings.GetProperty("externalApiKeyConfigured").GetBoolean());

        await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology graph-only frontend");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{graphFrontendPort}/healthz",
            bearerToken: null,
            StartupTimeout);
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_GraphOnlyModeRunsSqlBackedWorkload()
    {
        var sqlContainerName = ApplicationTopologyGraphSqlServerDockerBridge.GraphSqlServerContainerName;
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage) ||
            await DockerComposeStack.ContainerExistsAsync(sqlContainerName))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        var shouldCleanupSqlContainer = true;
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__GraphConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__GraphSecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
            Assert.DoesNotContain(
                resources,
                resource => resource.GetProperty("id").GetString() == "application:application-topology-sql-server");
            var graphStorage = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "cloudshell.storage:graph-application-topology-local");
            var graphVolume = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "cloudshell.volume:graph-application-topology-sql-data");
            var graphSqlServer = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-server:graph-application-topology-sql-server");
            var graphDatabase = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-database:graph-application-topology-db");
            var graphSettings = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "configuration.store:graph-application-topology-settings");
            var graphSecrets = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "secrets.vault:graph-application-topology-secrets");
            var graphApi = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.aspnet-core-project:graph-application-topology-api");
            var graphFrontend = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.aspnet-core-project:graph-application-topology-frontend");
            Assert.Equal("cloudshell.storage", graphStorage.GetProperty("typeId").GetString());
            Assert.Equal("cloudshell.volume", graphVolume.GetProperty("typeId").GetString());
            Assert.Contains(
                "cloudshell.storage:graph-application-topology-local",
                graphVolume.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

            await StartGraphResourceIfAvailableAsync(host, graphSqlServer, "ApplicationTopology graph-only SQL Server");
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:graph-application-topology-sql-server",
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");
            var sampleDataPath = Path.Combine(
                SampleProcess.FindRepositoryRoot(),
                "samples",
                "ApplicationTopology",
                "Host",
                "Data",
                "storage",
                "sql-server");
            Assert.True(
                Directory.Exists(sampleDataPath),
                $"Expected graph storage-backed SQL data path '{sampleDataPath}' to be created.");

            var ensureCreatedHref = graphDatabase
                .GetProperty("resourceActions")
                .GetProperty(SqlDatabaseResourceTypeProvider.Operations.EnsureCreated.Value)
                .GetProperty("href")
                .GetString() ?? throw new InvalidOperationException("The graph SQL database ensure-created action did not include an href.");
            await host.SendAsync(HttpMethod.Post, ensureCreatedHref);

            await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology graph-only settings");
            await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology graph-only secrets");
            await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology graph-only API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{graphApiPort}/health",
                bearerToken: null,
                StartupTimeout);
            var graphDatabaseJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{graphApiPort}/database",
                StartupTimeout);
            using var graphDatabaseDocument = JsonDocument.Parse(graphDatabaseJson);
            Assert.Equal("ok", graphDatabaseDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal("mssql", graphDatabaseDocument.RootElement.GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphDatabaseDocument.RootElement.GetProperty("database").GetString());

            await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology graph-only frontend");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{graphFrontendPort}/healthz",
                bearerToken: null,
                StartupTimeout);
            var graphUpstreamJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{graphFrontendPort}/upstream",
                StartupTimeout);
            using var graphUpstreamDocument = JsonDocument.Parse(graphUpstreamJson);
            var graphUpstream = graphUpstreamDocument.RootElement;
            Assert.Equal("Application Topology Frontend", graphUpstream.GetProperty("frontend").GetString());
            Assert.Equal("https+http://application-topology-api", graphUpstream.GetProperty("logicalApiEndpoint").GetString());
            Assert.Equal("Hello from the referenced API project.", graphUpstream.GetProperty("upstream").GetProperty("message").GetString());
            Assert.Equal("Graph", graphUpstream.GetProperty("settings").GetProperty("mode").GetString());
            Assert.True(graphUpstream.GetProperty("settings").GetProperty("externalApiKeyConfigured").GetBoolean());
            Assert.Equal("ok", graphUpstream.GetProperty("database").GetProperty("status").GetString());
            Assert.Equal("mssql", graphUpstream.GetProperty("database").GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphUpstream.GetProperty("database").GetProperty("database").GetString());

            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:graph-application-topology-sql-server");
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:graph-application-topology-sql-server",
                ResourceState.Stopped,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed after graph SQL stop.");
            shouldCleanupSqlContainer = false;
        }
        finally
        {
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:graph-application-topology-sql-server");
            if (shouldCleanupSqlContainer)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
            }
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_GraphOnlySqlRuntimeStopsOnGracefulHostShutdown()
    {
        const string graphSqlServerResourceId = "application.sql-server:graph-application-topology-sql-server";
        var sqlContainerName = ApplicationTopologyGraphSqlServerDockerBridge.GraphSqlServerContainerName;
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage))
        {
            return;
        }

        await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__GraphConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__GraphSecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var graphSqlServer = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == graphSqlServerResourceId);

            await StartGraphResourceIfAvailableAsync(host, graphSqlServer, "ApplicationTopology graph-only SQL Server");
            await WaitForResourceStateAsync(
                host,
                graphSqlServerResourceId,
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");

            await host.StopAsync(StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed during graceful host shutdown.");
        }
        finally
        {
            host.Dispose();
            await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_SqlInclusiveRuntimePathConnectsFrontendApiAndDatabase()
    {
        const string sqlContainerName = "cloudshell-application-application-topology-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.DefaultSqlServerImage) ||
            await DockerComposeStack.ContainerExistsAsync(sqlContainerName))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphApiPort = await GetFreePortAsync();
        var graphFrontendPort = await GetFreePortAsync();
        var sqlPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        var shouldCleanupSqlContainer = true;
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__GraphOnly", "false"),
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{graphApiPort}"),
                ("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{graphFrontendPort}"),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var graphSqlServer = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-server:graph-application-topology-sql-server");
            await StartGraphResourceIfAvailableAsync(host, graphSqlServer, "ApplicationTopology SQL Server");
            await WaitForResourceStateAsync(
                host,
                "application:application-topology-sql-server",
                ResourceState.Running,
                StartupTimeout);
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:graph-application-topology-sql-server",
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be created.");
            var startedSqlContainerId = await DockerComposeStack.GetContainerIdAsync(sqlContainerName) ??
                throw new InvalidOperationException(
                    $"Docker container '{sqlContainerName}' did not have an inspectable id.");
            await host.SendAsync(
                HttpMethod.Post,
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString("application.sql-server:graph-application-topology-sql-server")}/actions/restart?ignoreDependentWarning=true");
            await WaitForResourceStateAsync(
                host,
                "application:application-topology-sql-server",
                ResourceState.Running,
                StartupTimeout);
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:graph-application-topology-sql-server",
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerIdChangedAsync(
                    sqlContainerName,
                    startedSqlContainerId,
                    StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be recreated after graph SQL restart.");

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

            var graphDatabase = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-database:graph-application-topology-db");
            var graphApi = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "application.aspnet-core-project:graph-application-topology-api");
            var graphFrontend = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "application.aspnet-core-project:graph-application-topology-frontend");
            var ensureCreatedHref = graphDatabase
                .GetProperty("resourceActions")
                .GetProperty(SqlDatabaseResourceTypeProvider.Operations.EnsureCreated.Value)
                .GetProperty("href")
                .GetString() ?? throw new InvalidOperationException("The graph SQL database ensure-created action did not include an href.");
            await host.SendAsync(HttpMethod.Post, ensureCreatedHref);

            await StartGraphResourceIfAvailableAsync(host, Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "configuration.store:graph-application-topology-settings"), "ApplicationTopology settings");
            await StartGraphResourceIfAvailableAsync(host, Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() ==
                    "secrets.vault:graph-application-topology-secrets"), "ApplicationTopology secrets");
            await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{graphApiPort}/health",
                bearerToken: null,
                StartupTimeout);
            var graphSettingsJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{graphApiPort}/settings",
                StartupTimeout);
            using var graphSettingsDocument = JsonDocument.Parse(graphSettingsJson);
            var graphSettings = graphSettingsDocument.RootElement;
            Assert.Equal("Hello from CloudShell graph configuration.", graphSettings.GetProperty("message").GetString());
            Assert.Equal("Graph", graphSettings.GetProperty("mode").GetString());
            Assert.True(graphSettings.GetProperty("externalApiKeyConfigured").GetBoolean());
            var graphDatabaseJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{graphApiPort}/database",
                StartupTimeout);
            using var graphDatabaseDocument = JsonDocument.Parse(graphDatabaseJson);
            Assert.Equal("ok", graphDatabaseDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal("mssql", graphDatabaseDocument.RootElement.GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphDatabaseDocument.RootElement.GetProperty("database").GetString());

            await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology Frontend");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{graphFrontendPort}/healthz",
                bearerToken: null,
                StartupTimeout);
            var graphUpstreamJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{graphFrontendPort}/upstream",
                StartupTimeout);
            using var graphUpstreamDocument = JsonDocument.Parse(graphUpstreamJson);
            var graphUpstream = graphUpstreamDocument.RootElement;
            Assert.Equal("Application Topology Frontend", graphUpstream.GetProperty("frontend").GetString());
            Assert.Equal("https+http://application-topology-api", graphUpstream.GetProperty("logicalApiEndpoint").GetString());
            Assert.Equal("Hello from the referenced API project.", graphUpstream.GetProperty("upstream").GetProperty("message").GetString());
            Assert.Equal("Graph", graphUpstream.GetProperty("settings").GetProperty("mode").GetString());
            Assert.True(graphUpstream.GetProperty("settings").GetProperty("externalApiKeyConfigured").GetBoolean());
            Assert.Equal("ok", graphUpstream.GetProperty("database").GetProperty("status").GetString());
            Assert.Equal("mssql", graphUpstream.GetProperty("database").GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphUpstream.GetProperty("database").GetProperty("database").GetString());

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
            var graphEventsJson = await host.GetStringAsync(
                "/api/control-plane/v1/resource-events?resourceId=application.sql-server%3Agraph-application-topology-sql-server");
            using var graphEventsDocument = JsonDocument.Parse(graphEventsJson);
            var graphCredentialEvents = graphEventsDocument.RootElement
                .EnumerateArray()
                .Where(resourceEvent => string.Equals(
                    resourceEvent.GetProperty("eventType").GetString(),
                    "event.provider.applications.sql-server.credential.resolved",
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.NotEmpty(graphCredentialEvents);
            foreach (var graphCredentialEvent in graphCredentialEvents)
            {
                Assert.Equal(
                    "application.sql-server:graph-application-topology-sql-server",
                    graphCredentialEvent.GetProperty("resourceId").GetString());
                Assert.Equal("Information", graphCredentialEvent.GetProperty("severity").GetString());
                Assert.Contains(
                    "application_topology",
                    graphCredentialEvent.GetProperty("message").GetString() ?? string.Empty);
                Assert.DoesNotContain(
                    "Password=",
                    graphCredentialEvent.GetProperty("message").GetString() ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            }

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

            await StopResourceIfRunningAsync(host, "application:application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application:application-topology-api");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:graph-application-topology-sql-server");
            await WaitForResourceStateAsync(
                host,
                "application:application-topology-sql-server",
                ResourceState.Stopped,
                StartupTimeout);
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:graph-application-topology-sql-server",
                ResourceState.Stopped,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed after graph SQL stop.");
            shouldCleanupSqlContainer = false;
        }
        finally
        {
            await StopResourceIfRunningAsync(host, "application:application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application:application-topology-api");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.aspnet-core-project:graph-application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:graph-application-topology-sql-server");
            await StopResourceIfRunningAsync(host, "application:application-topology-sql-server");
            if (shouldCleanupSqlContainer)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
            }
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
                    ("ApplicationTopology__GraphOnly", "false"),
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
    public async Task SettingsAndSecretsSample_RunsServicesAndApiWithoutOldProviderRecords()
    {
        var configurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var secretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var apiEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        const string settingsResourceId = "configuration.store:sample-app";
        const string secretsResourceId = "secrets.vault:sample-app";
        const string apiResourceId = "application.aspnet-core-project:settings-secrets-api";
        using var host = await SampleProcess.StartAsync(
            "samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj",
            await GetFreePortAsync(),
            [
                ("Samples__SettingsAndSecrets__ConfigurationServiceEndpoint", configurationEndpoint),
                ("Samples__SettingsAndSecrets__SecretsServiceEndpoint", secretsEndpoint),
                ("Samples__SettingsAndSecrets__ApiEndpoint", apiEndpoint)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == settingsResourceId);
        var secrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == secretsResourceId);
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == apiResourceId);

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:sample-app");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets-vault:sample-app");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:settings-secrets-api");
        Assert.Equal(apiEndpoint, GetPrimaryEndpointAddress(api));
        Assert.Equal(
            "2",
            settings.GetProperty("attributes").GetProperty("configuration.entries.count").GetString());
        Assert.Equal(
            "1",
            secrets.GetProperty("attributes").GetProperty("secrets.entries.count").GetString());

        await StartGraphResourceIfAvailableAsync(host, settings, "SettingsAndSecrets settings");
        await StartGraphResourceIfAvailableAsync(host, secrets, "SettingsAndSecrets secrets");
        await StartGraphResourceIfAvailableAsync(host, api, "SettingsAndSecrets API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{configurationEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{secretsEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{apiEndpoint}/health",
            bearerToken: null,
            StartupTimeout);

        var configurationJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/configuration");
        using var configurationDocument = JsonDocument.Parse(configurationJson);
        Assert.Equal(
            "connected",
            configurationDocument.RootElement.GetProperty("status").GetString());
        Assert.Contains(
            configurationDocument.RootElement.GetProperty("entries").EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a configuration entry");

        var secretJson = await host.GetAbsoluteStringAsync(
            $"{apiEndpoint.TrimEnd('/')}/secrets/sample-api-key");
        using var secretDocument = JsonDocument.Parse(secretJson);
        Assert.Equal(
            "connected",
            secretDocument.RootElement.GetProperty("status").GetString());
        Assert.Equal(
            "local-development-api-key",
            secretDocument.RootElement.GetProperty("value").GetString());
    }

    private static void AssertGraphHealthRefreshSucceeded(
        JsonElement health,
        string resourceId)
    {
        Assert.Equal(resourceId, health.GetProperty("resourceId").GetString());
        Assert.Equal((int)ResourceHealthStatus.Healthy, health.GetProperty("status").GetInt32());
        Assert.Contains(
            health.GetProperty("checks").EnumerateArray(),
            check =>
                check.GetProperty("status").GetInt32() == (int)ResourceHealthStatus.Healthy &&
                check.GetProperty("check").GetProperty("type").GetInt32() == (int)ResourceProbeType.Health);
        Assert.Contains(
            health.GetProperty("checks").EnumerateArray(),
            check =>
                check.GetProperty("status").GetInt32() == (int)ResourceHealthStatus.Healthy &&
                check.GetProperty("check").GetProperty("type").GetInt32() == (int)ResourceProbeType.Liveness);
    }

    private static async Task StartGraphResourceIfAvailableAsync(
        SampleProcess host,
        JsonElement resource,
        string label)
    {
        if (resource.TryGetProperty("state", out var state) &&
            state.ValueKind == JsonValueKind.Number &&
            state.GetInt32() == (int)ResourceState.Running)
        {
            return;
        }

        if (resource.GetProperty("resourceActions").TryGetProperty("start", out var startAction))
        {
            var href = startAction.GetProperty("href").GetString() ??
                throw new InvalidOperationException($"The graph {label} start action did not include an href.");
            await host.SendAsync(HttpMethod.Post, href);
            return;
        }

        Assert.Equal((int)ResourceState.Running, resource.GetProperty("state").GetInt32());
    }

    private static async Task StopGraphResourceIfAvailableAsync(
        SampleProcess host,
        JsonElement resource,
        string label)
    {
        if (resource.TryGetProperty("state", out var state) &&
            state.ValueKind == JsonValueKind.Number &&
            state.GetInt32() == (int)ResourceState.Stopped)
        {
            return;
        }

        if (resource.GetProperty("resourceActions").TryGetProperty("stop", out var stopAction))
        {
            var href = stopAction.GetProperty("href").GetString() ??
                throw new InvalidOperationException($"The graph {label} stop action did not include an href.");
            await host.SendAsync(HttpMethod.Post, href);
            return;
        }

        Assert.Equal((int)ResourceState.Stopped, resource.GetProperty("state").GetInt32());
    }

    [Fact]
    public async Task ThirdPartyIdentitySample_ProjectsIdentityProvisioningBoundary()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ThirdPartyIdentity/CloudShell.ThirdPartyIdentity.csproj",
            await GetFreePortAsync(),
            [
                ("Authentication__Enabled", "false"),
                ("Authentication__OpenIdConnect__RequireHttpsMetadata", "false")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var provisioning = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "identity-provisioning:keycloak");
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:third-party-identity");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:keycloak-provisioned-api");
        var attributes = provisioning.GetProperty("attributes");
        var settingsAttributes = settings.GetProperty("attributes");
        var apiAttributes = api.GetProperty("attributes");

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:third-party-identity");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:keycloak-provisioned-api");
        Assert.Equal("cloudshell.identity-provisioning", provisioning.GetProperty("typeId").GetString());
        Assert.Equal("Keycloak Identity Provisioning", provisioning.GetProperty("displayName").GetString());
        Assert.Equal("identity-provisioning", attributes.GetProperty("infrastructure.kind").GetString());
        Assert.Equal("Keycloak", attributes.GetProperty("identity.provider").GetString());
        Assert.Equal("identity:keycloak", attributes.GetProperty("identity.providerId").GetString());
        Assert.Equal("oidc", attributes.GetProperty("identity.providerKind").GetString());
        Assert.Equal("configuration.store", settings.GetProperty("typeId").GetString());
        Assert.Equal("Third-party Identity Settings", settings.GetProperty("displayName").GetString());
        Assert.Equal("http://localhost:5138", settingsAttributes.GetProperty("configuration.endpoint").GetString());
        Assert.Equal("1", settingsAttributes.GetProperty("configuration.entries.count").GetString());
        Assert.Equal("application.aspnet-core-project", api.GetProperty("typeId").GetString());
        Assert.Equal("Keycloak Provisioned API", api.GetProperty("displayName").GetString());
        Assert.EndsWith(
            "/samples/ThirdPartyIdentity/Api/CloudShell.ThirdPartyIdentity.Api.csproj",
            apiAttributes.GetProperty("project.path").GetString());
        Assert.Equal("false", apiAttributes.GetProperty("project.hotReload").GetString());
        Assert.Equal("false", apiAttributes.GetProperty("project.useLaunchSettings").GetString());
        Assert.Equal("http://localhost:5235", GetPrimaryEndpointAddress(api));
        Assert.Contains(
            "configuration.store:third-party-identity",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal(
            "identity:keycloak",
            api.GetProperty("identity").GetProperty("providerId").GetString());
        Assert.Equal(
            "keycloak-provisioned-api",
            api.GetProperty("identity").GetProperty("name").GetString());
        var setupAction = provisioning
            .GetProperty("resourceActions")
            .GetProperty("identity.provisioning.setup");
        Assert.Equal("POST", setupAction.GetProperty("method").GetString());
        Assert.False(string.IsNullOrWhiteSpace(setupAction.GetProperty("href").GetString()));
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ThirdPartyIdentitySample_KeycloakProvisionedWorkloadReadsConfiguration()
    {
        if (!await DockerComposeStack.IsAvailableAsync())
        {
            return;
        }

        await CleanupThirdPartyIdentityKeycloakStacksAsync();
        var apiPort = await GetFreePortAsync();
        var configurationServiceBasePort = await GetServiceBasePortAsync(
            "configuration.store:third-party-identity");
        var configurationEndpoint =
            $"http://localhost:{configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)}";
        var root = SampleProcess.FindRepositoryRoot();
        var keycloak = await StartThirdPartyIdentityKeycloakAsync(
            root,
            "cloudshell-third-party-identity-test");
        var keycloakProjectName = keycloak.Stack.ProjectName;
        using var keycloakStack = keycloak.Stack;
        var keycloakPort = keycloak.Port;

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
                ("Samples__ThirdPartyIdentity__ConfigurationServiceEndpoint", configurationEndpoint)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var provisioning = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "identity-provisioning:keycloak");
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:third-party-identity");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.aspnet-core-project:keycloak-provisioned-api");

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration:third-party-identity");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:keycloak-provisioned-api");

        var setupHref = provisioning
            .GetProperty("resourceActions")
            .GetProperty("identity.provisioning.setup")
            .GetProperty("href")
            .GetString() ?? throw new InvalidOperationException(
                "The identity provisioning setup action did not include an href.");
        var setupJson = await host.SendAsync(HttpMethod.Post, setupHref);
        using var setupDocument = JsonDocument.Parse(setupJson);
        Assert.Contains(
            "Executed Identity Provisioning Setup",
            setupDocument.RootElement.GetProperty("message").GetString());

        await StartGraphResourceIfAvailableAsync(host, settings, "ThirdPartyIdentity settings");
        await StartGraphResourceIfAvailableAsync(host, api, "ThirdPartyIdentity API");

        var configurationJson = await WaitForJsonStatusAsync(
            $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/configuration",
            "connected",
            TimeSpan.FromMinutes(1));
        using var configurationDocument = JsonDocument.Parse(configurationJson);
        var configurationRoot = configurationDocument.RootElement;

        Assert.Equal("connected", configurationRoot.GetProperty("status").GetString());
        Assert.Equal("keycloak-provisioned-api", configurationRoot.GetProperty("clientId").GetString());
        Assert.Contains(
            configurationRoot.GetProperty("entries").EnumerateArray(),
            entry =>
                entry.GetProperty("name").GetString() == "Sample:Message" &&
                entry.GetProperty("value").GetString() == "Hello from a Keycloak-provisioned resource identity");

        keycloakStack.Dispose();
        Assert.True(
            await WaitForDockerComposeProjectRemovedAsync(keycloakProjectName, StartupTimeout),
            $"Expected ThirdPartyIdentity Keycloak compose project '{keycloakProjectName}' to be removed after cleanup.");
    }

    [Fact]
    public async Task SplitHostingSample_RendersResourceThroughRemoteControlPlane()
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
        var resources = document.RootElement.EnumerateArray().ToArray();
        var network = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "network:split-sample");
        Assert.Equal("cloudshell.network", network.GetProperty("typeId").GetString());
        Assert.Equal("Logical", network.GetProperty("attributes").GetProperty("network.kind").GetString());
        Assert.Equal("logicalOnly", network.GetProperty("attributes").GetProperty("network.hostReadiness").GetString());
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
    public async Task ContainerAppDeploymentSample_UpdatesContainerAppState()
    {
        const string sampleImage = "cloudshell/mock-api:20260608.1";
        const string containerAppResourceId = "application.container-app:sample-api";
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
        var docker = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:sample");
        var registry = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker.container:sample-registry");
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == containerAppResourceId);

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:container:sample-registry");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:sample-api");

        var registryAddress = $"localhost:{registryPort.ToString(CultureInfo.InvariantCulture)}";
        var dockerAttributes = docker.GetProperty("attributes");
        var registryAttributes = registry.GetProperty("attributes");
        var appAttributes = app.GetProperty("attributes");
        Assert.Equal("docker.host", docker.GetProperty("typeId").GetString());
        Assert.Equal(registryAddress, dockerAttributes.GetProperty("container.registry").GetString());
        Assert.Equal("docker.container", registry.GetProperty("typeId").GetString());
        Assert.Equal("registry:2", registryAttributes.GetProperty("container.image").GetString());
        Assert.Equal(registryAddress, registryAttributes.GetProperty("container.registry").GetString());
        Assert.Equal("application.container-app", app.GetProperty("typeId").GetString());
        Assert.Equal(sampleImage, appAttributes.GetProperty("container.image").GetString());
        Assert.Equal(registryAddress, appAttributes.GetProperty("container.registry").GetString());

        var updateJson = await host.SendJsonAsync(
            HttpMethod.Post,
            $"/api/container-apps/v1/{Uri.EscapeDataString(containerAppResourceId)}/deployments",
            """
            {
              "image": "cloudshell/mock-api:20260608.4",
              "triggeredBy": "sample-smoke-test",
              "requestedReplicas": 2
            }
            """);
        using var updateDocument = JsonDocument.Parse(updateJson);

        Assert.Contains(
            "cloudshell/mock-api:20260608.4",
            updateDocument.RootElement.GetProperty("message").GetString());

        var updatedJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(containerAppResourceId)}");
        using var updatedDocument = JsonDocument.Parse(updatedJson);
        var updatedAttributes = updatedDocument.RootElement.GetProperty("attributes");
        Assert.Equal(
            "cloudshell/mock-api:20260608.4",
            updatedAttributes.GetProperty("container.image").GetString());
        Assert.Equal(
            "2",
            updatedAttributes.GetProperty("container.replicas").GetString());

        var replicaUpdateJson = await host.SendJsonAsync(
            HttpMethod.Put,
            $"/api/container-apps/v1/{Uri.EscapeDataString(containerAppResourceId)}/replicas",
            """
            {
              "replicas": 3,
              "restartIfRunning": false,
              "triggeredBy": "sample-smoke-test"
            }
            """);
        using var replicaUpdateDocument = JsonDocument.Parse(replicaUpdateJson);

        Assert.Contains(
            "3",
            replicaUpdateDocument.RootElement.GetProperty("message").GetString());

        var scaledJson = await host.GetStringAsync(
            $"/api/control-plane/v1/resources/{Uri.EscapeDataString(containerAppResourceId)}");
        using var scaledDocument = JsonDocument.Parse(scaledJson);
        var scaledAttributes = scaledDocument.RootElement.GetProperty("attributes");
        Assert.Equal(
            "cloudshell/mock-api:20260608.4",
            scaledAttributes.GetProperty("container.image").GetString());
        Assert.Equal(
            "3",
            scaledAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("true", scaledAttributes.GetProperty(ResourceAttributeNames.ContainerReplicasEnabled).GetString());
        Assert.Equal("3", scaledAttributes.GetProperty(ResourceAttributeNames.DeploymentRequestedReplicaSlots).GetString());

        var deploymentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(containerAppResourceId)}/details?tab={Uri.EscapeDataString("application:deployment")}");
        Assert.Contains("Replicated", deploymentHtml);
        Assert.Contains("3 replica slots", deploymentHtml);

        var scalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(containerAppResourceId)}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        Assert.Contains("Replica slots", scalingHtml);
        Assert.Contains("Slot 1", scalingHtml);
        Assert.Contains("Slot 2", scalingHtml);
        Assert.Contains("Slot 3", scalingHtml);
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ContainerAppDeploymentSample_StartsRegistryRuntime()
    {
        var registryContainerName =
            ContainerAppDeploymentDockerContainerRuntimeHandler.RegistryContainerName;
        if (!await DockerComposeStack.IsAvailableAsync() ||
            await DockerComposeStack.ContainerExistsAsync(registryContainerName))
        {
            return;
        }

        const string registryResourceId = "docker.container:sample-registry";
        var registryPort = await GetFreePortAsync();
        using var host = await SampleProcess.StartAsync(
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            await GetFreePortAsync(),
            [
                ("ContainerAppDeployment__EnableDockerRuntime", "true"),
                ("ContainerAppDeployment__RegistryPort", registryPort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
            var registry = Assert.Single(resources, resource =>
                resource.GetProperty("id").GetString() == registryResourceId);

            Assert.DoesNotContain(resources, resource =>
                resource.GetProperty("id").GetString() == "docker:container:sample-registry");

            await StartGraphResourceIfAvailableAsync(
                host,
                registry,
                "ContainerAppDeployment registry");

            Assert.True(
                await WaitForDockerContainerExistsAsync(registryContainerName, StartupTimeout),
                $"Expected Docker container '{registryContainerName}' to be created.");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{registryPort.ToString(CultureInfo.InvariantCulture)}/v2/",
                bearerToken: null,
                StartupTimeout);

            var startedRegistryJson = await host.GetStringAsync(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(registryResourceId)}");
            using var startedRegistryDocument = JsonDocument.Parse(startedRegistryJson);
            await StopGraphResourceIfAvailableAsync(
                host,
                startedRegistryDocument.RootElement,
                "ContainerAppDeployment registry");

            Assert.True(
                await WaitForDockerContainerRemovedAsync(registryContainerName, StartupTimeout),
                $"Expected Docker container '{registryContainerName}' to be removed after registry stop.");
        }
        finally
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(registryContainerName);
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ContainerAppDeploymentSample_RegistryRuntimeStopsOnGracefulHostShutdown()
    {
        const string registryResourceId = "docker.container:sample-registry";
        var registryContainerName =
            ContainerAppDeploymentDockerContainerRuntimeHandler.RegistryContainerName;
        if (!await DockerComposeStack.IsAvailableAsync())
        {
            return;
        }

        await DockerComposeStack.RemoveContainerIfExistsAsync(registryContainerName);

        var registryPort = await GetFreePortAsync();
        var host = await SampleProcess.StartAsync(
            "samples/ContainerAppDeployment/CloudShell.ContainerAppDeployment.csproj",
            await GetFreePortAsync(),
            [
                ("ContainerAppDeployment__EnableDockerRuntime", "true"),
                ("ContainerAppDeployment__RegistryPort", registryPort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var registry = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == registryResourceId);

            await StartGraphResourceIfAvailableAsync(
                host,
                registry,
                "ContainerAppDeployment registry");
            await WaitForResourceStateAsync(
                host,
                registryResourceId,
                ResourceState.Running,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerExistsAsync(registryContainerName, StartupTimeout),
                $"Expected Docker container '{registryContainerName}' to be created.");

            await host.StopAsync(StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(registryContainerName, StartupTimeout),
                $"Expected Docker container '{registryContainerName}' to be removed during graceful host shutdown.");
        }
        finally
        {
            host.Dispose();
            await DockerComposeStack.RemoveContainerIfExistsAsync(registryContainerName);
        }
    }

    [Fact]
    public async Task ReplicatedContainerHealthSample_ProjectsReplicaHealthIntoParentAssessment()
    {
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync(),
            [
                ("ReplicatedContainerHealth__GraphOnly", "false")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var app = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        var graphDocker = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:graph-sample");
        var graphApp = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:graph-api");
        var appAttributes = app.GetProperty("attributes");
        var graphAppAttributes = graphApp.GetProperty("attributes");

        Assert.Equal("true", appAttributes.GetProperty(ResourceAttributeNames.ContainerReplicasEnabled).GetString());
        Assert.Equal("3", appAttributes.GetProperty(ResourceAttributeNames.ContainerReplicas).GetString());
        Assert.Equal("3", appAttributes.GetProperty(ResourceAttributeNames.DeploymentMaterializedReplicas).GetString());
        Assert.Equal("3", appAttributes.GetProperty(ResourceAttributeNames.DeploymentProjectedReplicas).GetString());
        Assert.Equal("docker.host", graphDocker.GetProperty("typeId").GetString());
        Assert.Equal("application.container-app", graphApp.GetProperty("typeId").GetString());
        Assert.Equal("cloudshell-application-api:20260622.2", graphAppAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", graphAppAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("http://localhost:5092", GetPrimaryEndpointAddress(graphApp));
        Assert.Equal("8080", graphApp.GetProperty("endpoints")
            .EnumerateArray()
            .Single(endpoint => endpoint.GetProperty("name").GetString() == "http")
            .GetProperty("targetPort")
            .GetInt32()
            .ToString(CultureInfo.InvariantCulture));
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.Liveness.ToString());
        Assert.Contains(
            "docker:graph-sample",
            graphApp.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        var graphTemplateJson = await host.GetStringAsync(
            "/api/control-plane/v1/resource-groups/replicated-container-health-graph-poc/template");
        using var graphTemplateDocument = JsonDocument.Parse(graphTemplateJson);
        var graphTemplate = graphTemplateDocument.RootElement.GetProperty("template");
        var graphTemplateResources = graphTemplate.GetProperty("resources")
            .EnumerateArray()
            .ToArray();
        var graphAppTemplate = Assert.Single(graphTemplateResources, resource =>
            resource.GetProperty("resourceId").GetString() == "application.container-app:graph-api");
        var graphAppDefinition = graphAppTemplate.GetProperty("configuration");
        var graphAppDefinitionAttributes = graphAppDefinition.GetProperty("attributes");

        Assert.Equal("resourceGroup", graphTemplate.GetProperty("kind").GetString());
        Assert.Equal("resource-model", graphAppTemplate.GetProperty("providerId").GetString());
        Assert.Equal("application.container-app", graphAppTemplate.GetProperty("resourceType").GetString());
        Assert.Equal("resource-definition.v1", graphAppTemplate.GetProperty("providerConfigurationVersion").GetString());
        Assert.Equal("application.container-app", graphAppDefinition.GetProperty("typeId").GetString());
        Assert.Equal("applications.container-app", graphAppDefinition.GetProperty("providerId").GetString());
        Assert.Equal("Graph Replicated API", graphAppDefinition.GetProperty("displayName").GetString());
        Assert.Equal("cloudshell-application-api:20260622.2", graphAppDefinitionAttributes.GetProperty("container.image").GetString());
        Assert.Equal(3, graphAppDefinitionAttributes.GetProperty("container.replicas").GetInt32());
        Assert.Equal(
            "docker:graph-sample",
            Assert.Single(graphAppDefinition.GetProperty("dependsOn").EnumerateArray())
                .GetProperty("value")
                .GetString());
        Assert.True(graphAppDefinition.GetProperty("capabilities")
            .TryGetProperty(ResourceHealthCheckCapabilityIds.HealthChecks.ToString(), out _));

        var observability = app.GetProperty("observability");
        Assert.True(observability.GetProperty("logs").GetBoolean());
        Assert.True(observability.GetProperty("traces").GetBoolean());
        Assert.True(observability.GetProperty("metrics").GetBoolean());
        Assert.Equal("http", observability.GetProperty("otlpEndpoint").GetString()?[..4]);
        var telemetryScopes = observability
            .GetProperty("scopes")
            .EnumerateArray()
            .OrderBy(scope => scope.GetProperty("scopeResourceId").GetString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Collection(
            telemetryScopes,
            scope => Assert.Equal("runtime-container:application-api:replica-1", scope.GetProperty("scopeResourceId").GetString()),
            scope => Assert.Equal("runtime-container:application-api:replica-2", scope.GetProperty("scopeResourceId").GetString()),
            scope => Assert.Equal("runtime-container:application-api:replica-3", scope.GetProperty("scopeResourceId").GetString()));

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

        var logSourcesJson = await host.GetStringAsync(
            $"/api/control-plane/v1/log-sources?resourceId={Uri.EscapeDataString("application:api")}");
        using var logSourcesDocument = JsonDocument.Parse(logSourcesJson);
        var replicaLogSources = logSourcesDocument.RootElement.EnumerateArray()
            .Where(source =>
                source.TryGetProperty("producerResourceId", out var producer) &&
                producer.GetString() == "application:api")
            .OrderBy(source => source.GetProperty("resourceId").GetString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Collection(
            replicaLogSources,
            source =>
            {
                Assert.Equal("Replica 1 logs", source.GetProperty("name").GetString());
                Assert.Equal("runtime-container:application-api:replica-1", source.GetProperty("resourceId").GetString());
            },
            source =>
            {
                Assert.Equal("Replica 2 logs", source.GetProperty("name").GetString());
                Assert.Equal("runtime-container:application-api:replica-2", source.GetProperty("resourceId").GetString());
            },
            source =>
            {
                Assert.Equal("Replica 3 logs", source.GetProperty("name").GetString());
                Assert.Equal("runtime-container:application-api:replica-3", source.GetProperty("resourceId").GetString());
            });

        var logsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}");
        Assert.Contains("Telemetry", logsHtml);
        Assert.Contains("Replica 1 logs", logsHtml);
        Assert.Contains("Replica 2 logs", logsHtml);
        Assert.Contains("Replica 3 logs", logsHtml);

        var logSourcesHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Logs.Value)}&logView=sources");
        Assert.Contains(
            "href=\"/resources/application%3Aapi/logs?logSourceId=runtime-container%3Aapplication-api%3Areplica-1%3Alogs",
            logSourcesHtml);
        Assert.DoesNotContain(
            "href=\"/resources/runtime-container%3Aapplication-api%3Areplica-1/logs",
            logSourcesHtml);

        var tracesHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Traces.Value)}");
        Assert.Contains("Telemetry", tracesHtml);
        Assert.Contains("Replica 1", tracesHtml);
        Assert.Contains("Replica 2", tracesHtml);
        Assert.Contains("Replica 3", tracesHtml);

        var metricsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}");
        Assert.Contains("Telemetry", metricsHtml);
        Assert.Contains("Replica 1", metricsHtml);
        Assert.Contains("Replica 2", metricsHtml);
        Assert.Contains("Replica 3", metricsHtml);

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
        Assert.Contains("Scale and replicas", healthHtml);
        Assert.Contains("href=\"/resources/application%3Aapi/scale-replicas\"", healthHtml);

        var globalHealthHtml = await host.GetStringAsync("/health");
        Assert.Contains("api", globalHealthHtml);
        Assert.Contains("runtime scope check(s)", globalHealthHtml);
        Assert.Contains("href=\"/resources/application%3Aapi/health\"", globalHealthHtml);

        var scalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application:api")}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        Assert.Contains("Health</th>", scalingHtml);
        Assert.Contains("health: No matching HTTP endpoint", scalingHtml);
    }

    [Fact]
    public async Task ReplicatedContainerHealthSample_GraphOnlyModeDeclaresGraphResourcesWithoutOldProviderResources()
    {
        var apiPort = await GetFreePortAsync();
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync(),
            [
                ("ReplicatedContainerHealth__ApiPort", apiPort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphDocker = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:graph-sample");
        var graphApp = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:graph-api");
        var graphAppAttributes = graphApp.GetProperty("attributes");

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "docker:sample");
        Assert.Equal("docker.host", graphDocker.GetProperty("typeId").GetString());
        Assert.Equal("application.container-app", graphApp.GetProperty("typeId").GetString());
        Assert.Equal(
            "cloudshell-application-api:20260622.2",
            graphAppAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", graphAppAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("true", graphAppAttributes.GetProperty(ResourceAttributeNames.ContainerReplicasEnabled).GetString());
        Assert.Equal("3", graphAppAttributes.GetProperty(ResourceAttributeNames.DeploymentRequestedReplicaSlots).GetString());
        Assert.Equal(
            $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}",
            GetPrimaryEndpointAddress(graphApp));
        Assert.Contains(
            "docker:graph-sample",
            graphApp.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.Liveness.ToString());

        var graphScalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:graph-api")}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        Assert.Contains("Scale and replicas", graphScalingHtml);
        Assert.Contains("Requested replica slots", graphScalingHtml);
        Assert.Contains("Replica slots", graphScalingHtml);
        Assert.Contains("Update replicas", graphScalingHtml);
        Assert.Contains("Slot 1", graphScalingHtml);
        Assert.Contains("Slot 2", graphScalingHtml);
        Assert.Contains("Slot 3", graphScalingHtml);
        Assert.Contains(">3</dd>", graphScalingHtml);

        var graphDeploymentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:graph-api")}/details?tab={Uri.EscapeDataString("application:deployment")}");
        Assert.Contains("Deploy image", graphDeploymentHtml);
        Assert.Contains("Current image", graphDeploymentHtml);
        Assert.Contains("Replicated", graphDeploymentHtml);
        Assert.Contains("3 replica slots", graphDeploymentHtml);

        var graphRevisionsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:graph-api")}/details?tab={Uri.EscapeDataString("application:revisions")}");
        Assert.Contains("No revisions recorded", graphRevisionsHtml);

        var graphMonitoringHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:graph-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value)}");
        Assert.Contains("Monitoring", graphMonitoringHtml);
    }

    [Fact]
    public async Task ReplicatedContainerHealthSample_GraphImageUpdateDelegatesToRuntimeAppConfiguration()
    {
        const string graphApiResourceId = "application.container-app:graph-api";
        const string runtimeApiResourceId = "application:api";
        const string updatedImage = "cloudshell-application-api:20260622.3";
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync(),
            [
                ("ReplicatedContainerHealth__GraphOnly", "false")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var graphApplyJson = await host.SendJsonAsync(
            HttpMethod.Post,
            $"/replicated-container-health/resource-graph/resources/{Uri.EscapeDataString(graphApiResourceId)}/container-image",
            $$"""
            {
              "image": "{{updatedImage}}"
            }
            """);
        using var graphApplyDocument = JsonDocument.Parse(graphApplyJson);
        var graphApply = graphApplyDocument.RootElement;
        Assert.True(graphApply.GetProperty("committed").GetBoolean());
        Assert.False(graphApply.GetProperty("hasErrors").GetBoolean());
        Assert.Equal("Committed", graphApply.GetProperty("status").GetString());
        Assert.True(graphApply.GetProperty("resultVersion").GetInt64() >
            graphApply.GetProperty("baseVersion").GetInt64());

        var graphResourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var graphResourcesDocument = JsonDocument.Parse(graphResourcesJson);
        var graphResources = graphResourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphApp = Assert.Single(graphResources, resource =>
            resource.GetProperty("id").GetString() == graphApiResourceId);

        Assert.Equal(
            updatedImage,
            graphApp.GetProperty("attributes").GetProperty("container.image").GetString());

        var updateImageAction = graphApp
            .GetProperty("resourceActions")
            .GetProperty("container.image.update");
        var updateImageHref = updateImageAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The graph container image update action did not include an href.");
        await host.SendAsync(HttpMethod.Post, updateImageHref);

        var runtimeResourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var runtimeResourcesDocument = JsonDocument.Parse(runtimeResourcesJson);
        var runtimeApp = Assert.Single(
            runtimeResourcesDocument.RootElement.EnumerateArray(),
            resource => resource.GetProperty("id").GetString() == runtimeApiResourceId);
        var runtimeAttributes = runtimeApp.GetProperty("attributes");

        Assert.Equal(updatedImage, runtimeAttributes.GetProperty(ResourceAttributeNames.ContainerImage).GetString());
        Assert.Equal("3", runtimeAttributes.GetProperty(ResourceAttributeNames.ContainerReplicas).GetString());

        var graphReplicaUpdateJson = await host.SendJsonAsync(
            HttpMethod.Put,
            $"/api/container-apps/v1/{Uri.EscapeDataString(graphApiResourceId)}/replicas",
            """
            {
              "replicas": 2,
              "restartIfRunning": false,
              "triggeredBy": "sample-smoke-test"
            }
            """);
        using var graphReplicaUpdateDocument = JsonDocument.Parse(graphReplicaUpdateJson);

        Assert.Contains(
            "2",
            graphReplicaUpdateDocument.RootElement.GetProperty("message").GetString());

        var scaledResourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var scaledResourcesDocument = JsonDocument.Parse(scaledResourcesJson);
        var scaledResources = scaledResourcesDocument.RootElement.EnumerateArray().ToArray();
        var scaledGraphApp = Assert.Single(scaledResources, resource =>
            resource.GetProperty("id").GetString() == graphApiResourceId);
        var scaledRuntimeApp = Assert.Single(scaledResources, resource =>
            resource.GetProperty("id").GetString() == runtimeApiResourceId);

        Assert.Equal(
            "2",
            scaledGraphApp.GetProperty("attributes").GetProperty("container.replicas").GetString());
        Assert.Equal(
            "2",
            scaledRuntimeApp.GetProperty("attributes").GetProperty(ResourceAttributeNames.ContainerReplicas).GetString());
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ReplicatedContainerHealthSample_GraphOnlyImageAndReplicaUpdatesRestartGraphContainers()
    {
        const string graphApiResourceId = "application.container-app:graph-api";
        const string updatedImage = "cloudshell-application-api:20260622.3";
        string[] containerNames =
        [
            "cloudshell-replicated-health-graph-api-replica-1",
            "cloudshell-replicated-health-graph-api-replica-2",
            "cloudshell-replicated-health-graph-api-replica-3"
        ];
        string[] scaledContainerNames =
        [
            "cloudshell-replicated-health-graph-api-replica-1",
            "cloudshell-replicated-health-graph-api-replica-2"
        ];
        const string ingressContainerName = "cloudshell-replicated-health-graph-api-ingress";
        var runtimeContainerNames = containerNames
            .Append(ingressContainerName)
            .ToArray();
        if (!await DockerComposeStack.IsAvailableAsync() ||
            await AnyDockerContainerExistsAsync(runtimeContainerNames))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var graphOnlySmokeTimeout = TimeSpan.FromSeconds(180);
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync(),
            [
                ("ReplicatedContainerHealth__ApiPort", apiPort.ToString(CultureInfo.InvariantCulture)),
                ("ReplicatedContainerHealth__GraphOnlyStatusCacheMilliseconds", "25"),
                ("ReplicatedContainerHealth__GraphOnlyReplicaCleanupLimit", "3")
            ],
            bindToAnyAddress: true);

        try
        {
            await host.WaitForHttpOkAsync("/api/control-plane/v1/resources", graphOnlySmokeTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var graphApp = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == graphApiResourceId);

            Assert.DoesNotContain(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == "application:api");
            Assert.DoesNotContain(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == "docker:sample");

            await StartGraphResourceIfAvailableAsync(host, graphApp, "ReplicatedContainerHealth graph-only API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                bearerToken: null,
                graphOnlySmokeTimeout);
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/work",
                bearerToken: null,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaLogSourcesAsync(host, graphApiResourceId, expectedReplicas: 3);
            var graphReplicaLogEntries = await WaitForAnyGraphReplicaLogEntriesAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                message => message.Contains("handled demo work", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(graphReplicaLogEntries);
            await AssertGraphResourceHealthChecksHealthyAsync(host, graphApiResourceId, apiPort, graphOnlySmokeTimeout);
            await AssertGraphResourceRuntimeHealthAggregatesAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaRuntimeEnvironmentAsync(
                "cloudshell-replicated-health-graph-api-replica-1",
                replica: 1);
            await AssertGraphReplicaTelemetryAsync(host, replica: 1, graphOnlySmokeTimeout);
            await AssertAnyGraphReplicaWorkTraceAsync(host, expectedReplicas: 3, graphOnlySmokeTimeout);
            await AssertGraphReplicaResourceObservabilityAsync(host, replica: 1);
            foreach (var containerName in containerNames)
            {
                Assert.True(
                    await WaitForDockerContainerExistsAsync(containerName, graphOnlySmokeTimeout),
                    $"Expected Docker container '{containerName}' to be created.");
            }
            Assert.True(
                await WaitForDockerContainerExistsAsync(ingressContainerName, graphOnlySmokeTimeout),
                $"Expected Docker ingress container '{ingressContainerName}' to be created.");
            var startedContainerIds = await GetDockerContainerIdsAsync(containerNames);

            var graphApplyJson = await host.SendJsonAsync(
                HttpMethod.Post,
                $"/replicated-container-health/resource-graph/resources/{Uri.EscapeDataString(graphApiResourceId)}/container-image",
                $$"""
                {
                  "image": "{{updatedImage}}"
                }
                """);
            using var graphApplyDocument = JsonDocument.Parse(graphApplyJson);

            Assert.True(graphApplyDocument.RootElement.GetProperty("committed").GetBoolean());
            Assert.False(graphApplyDocument.RootElement.GetProperty("hasErrors").GetBoolean());

            var updatedResourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var updatedResourcesDocument = JsonDocument.Parse(updatedResourcesJson);
            var updatedGraphApp = Assert.Single(
                updatedResourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == graphApiResourceId);
            Assert.Equal(
                updatedImage,
                updatedGraphApp.GetProperty("attributes").GetProperty("container.image").GetString());

            var updateImageAction = updatedGraphApp
                .GetProperty("resourceActions")
                .GetProperty("container.image.update");
            var updateImageHref = updateImageAction.GetProperty("href").GetString() ??
                throw new InvalidOperationException("The graph-only container image update action did not include an href.");
            await host.SendAsync(HttpMethod.Post, updateImageHref);
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                bearerToken: null,
                graphOnlySmokeTimeout);
            foreach (var containerName in containerNames)
            {
                Assert.True(
                    await WaitForDockerContainerIdChangedAsync(
                        containerName,
                        startedContainerIds[containerName],
                        graphOnlySmokeTimeout),
                    $"Expected Docker container '{containerName}' to be recreated after graph image update.");
            }
            var imageUpdatedContainerIds = await GetDockerContainerIdsAsync(containerNames);

            var graphReplicaUpdateJson = await host.SendJsonAsync(
                HttpMethod.Put,
                $"/api/container-apps/v1/{Uri.EscapeDataString(graphApiResourceId)}/replicas",
                """
                {
                  "replicas": 2,
                  "restartIfRunning": false,
                  "triggeredBy": "graph-only-smoke-test"
                }
                """);
            using var graphReplicaUpdateDocument = JsonDocument.Parse(graphReplicaUpdateJson);

            Assert.Contains(
                "2",
                graphReplicaUpdateDocument.RootElement.GetProperty("message").GetString());
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                bearerToken: null,
                graphOnlySmokeTimeout);
            await AssertGraphResourceHealthChecksHealthyAsync(host, graphApiResourceId, apiPort, graphOnlySmokeTimeout);
            await AssertGraphResourceRuntimeHealthAggregatesAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 2,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaLogSourcesAsync(host, graphApiResourceId, expectedReplicas: 2);
            foreach (var containerName in scaledContainerNames)
            {
                Assert.True(
                    await WaitForDockerContainerIdChangedAsync(
                        containerName,
                        imageUpdatedContainerIds[containerName],
                        graphOnlySmokeTimeout),
                    $"Expected Docker container '{containerName}' to be recreated after graph replica update.");
            }

            Assert.True(
                await WaitForDockerContainerRemovedAsync(
                    "cloudshell-replicated-health-graph-api-replica-3",
                    graphOnlySmokeTimeout),
                "Expected stale graph replica 3 to be removed after graph scale-down.");

            var scaledResourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var scaledResourcesDocument = JsonDocument.Parse(scaledResourcesJson);
            var scaledGraphApp = Assert.Single(
                scaledResourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == graphApiResourceId);

            Assert.Equal(
                "2",
                scaledGraphApp.GetProperty("attributes").GetProperty("container.replicas").GetString());

            await StopGraphResourceIfAvailableAsync(
                host,
                scaledGraphApp,
                "ReplicatedContainerHealth graph-only API");

            foreach (var containerName in runtimeContainerNames)
            {
                Assert.True(
                    await WaitForDockerContainerRemovedAsync(containerName, graphOnlySmokeTimeout),
                    $"Expected graph-only runtime container '{containerName}' to be removed after graph stop.");
            }
        }
        finally
        {
            await StopResourceIfRunningAsync(host, graphApiResourceId);
            foreach (var containerName in runtimeContainerNames)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(containerName);
            }
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ReplicatedContainerHealthSample_GraphContainerAppStartStopAndRestartDelegateToRuntimeApp()
    {
        string[] containerNames =
        [
            "cloudshell-application-api-20260622-2-replica-1",
            "cloudshell-application-api-20260622-2-replica-2",
            "cloudshell-application-api-20260622-2-replica-3"
        ];
        if (!await DockerComposeStack.IsAvailableAsync() ||
            await AnyDockerContainerExistsAsync(containerNames))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var shouldCleanupContainers = true;
        using var host = await SampleProcess.StartAsync(
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            await GetFreePortAsync(),
            [
                ("ReplicatedContainerHealth__GraphOnly", "false"),
                ("ReplicatedContainerHealth__ApiPort", apiPort.ToString(CultureInfo.InvariantCulture))
            ]);

        try
        {
            await host.WaitForHttpOkAsync("/", StartupTimeout);

            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var graphApp = Assert.Single(
                resourcesDocument.RootElement.EnumerateArray(),
                resource => resource.GetProperty("id").GetString() == "application.container-app:graph-api");

            Assert.Equal($"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}", GetPrimaryEndpointAddress(graphApp));

            await StartGraphResourceIfAvailableAsync(host, graphApp, "ReplicatedContainerHealth API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                bearerToken: null,
                StartupTimeout);
            foreach (var containerName in containerNames)
            {
                Assert.True(
                    await WaitForDockerContainerExistsAsync(containerName, StartupTimeout),
                    $"Expected Docker container '{containerName}' to be created.");
            }
            var startedContainerIds = await GetDockerContainerIdsAsync(containerNames);

            var restartAction = graphApp
                .GetProperty("resourceActions")
                .GetProperty(ResourceActionIds.Restart);
            var restartHref = restartAction.GetProperty("href").GetString() ??
                throw new InvalidOperationException("The graph container app restart action did not include an href.");
            await host.SendAsync(HttpMethod.Post, restartHref);
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                bearerToken: null,
                StartupTimeout);
            foreach (var containerName in containerNames)
            {
                Assert.True(
                    await WaitForDockerContainerIdChangedAsync(
                        containerName,
                        startedContainerIds[containerName],
                        StartupTimeout),
                    $"Expected Docker container '{containerName}' to be recreated after graph restart.");
            }

            var stopAction = graphApp
                .GetProperty("resourceActions")
                .GetProperty(ResourceActionIds.Stop);
            var stopHref = stopAction.GetProperty("href").GetString() ??
                throw new InvalidOperationException("The graph container app stop action did not include an href.");
            await host.SendAsync(HttpMethod.Post, stopHref);
            await WaitForResourceStateAsync(
                host,
                "application.container-app:graph-api",
                ResourceState.Stopped,
                StartupTimeout);
            foreach (var containerName in containerNames)
            {
                Assert.True(
                    await WaitForDockerContainerRemovedAsync(containerName, StartupTimeout),
                    $"Expected Docker container '{containerName}' to be removed after graph stop.");
            }

            shouldCleanupContainers = false;
        }
        finally
        {
            await StopResourceIfRunningAsync(host, "application.container-app:graph-api");
            await StopResourceIfRunningAsync(host, "application:api");
            if (shouldCleanupContainers)
            {
                foreach (var containerName in containerNames)
                {
                    await DockerComposeStack.RemoveContainerIfExistsAsync(containerName);
                }
            }
        }
    }

    [Fact]
    public async Task HostVirtualNetworkSample_ReconcilesEndpointMappingThroughRuntimeBridge()
    {
        const string apiResourceId = "application.aspnet-core-project:vnet-api";
        const string networkResourceId = "network:sample-vnet";
        var targetPort = await GetFreePortAsync();
        var virtualNetworkPort = await GetFreePortAsync();
        using var host = await SampleProcess.StartAsync(
            "samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj",
            await GetFreePortAsync(),
            [
                ("HostVirtualNetwork__TargetPort", targetPort.ToString(CultureInfo.InvariantCulture)),
                ("HostVirtualNetwork__VirtualNetworkPort", virtualNetworkPort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);
        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:vnet-api");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == apiResourceId);
        var network = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == networkResourceId);

        try
        {
            await StartGraphResourceIfAvailableAsync(host, api, "HostVirtualNetwork API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{targetPort.ToString(CultureInfo.InvariantCulture)}/health",
                null,
                StartupTimeout);

            var reconcile = network
                .GetProperty("resourceActions")
                .GetProperty("reconcileEndpointMappings");
            var href = reconcile.GetProperty("href").GetString() ??
                throw new InvalidOperationException("The virtual network reconcile action did not include an href.");
            await host.SendAsync(HttpMethod.Post, href);

            var healthJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{virtualNetworkPort.ToString(CultureInfo.InvariantCulture)}/health",
                StartupTimeout);
            using var healthDocument = JsonDocument.Parse(healthJson);
            Assert.Equal("ok", healthDocument.RootElement.GetProperty("status").GetString());
        }
        finally
        {
            await StopResourceIfRunningAsync(host, apiResourceId);
        }
    }

    [Fact]
    public async Task LoadBalancerSample_RunsLoadBalancerAndDnsPathsWithoutOldProviderRecords()
    {
        var root = SampleProcess.FindRepositoryRoot();
        var dataDirectory = Path.Combine(root, "samples", "LoadBalancer", "Data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        var hostsFilePath = Path.Combine(dataDirectory, "cloudshell.hosts");
        using var host = await SampleProcess.StartAsync(
            "samples/LoadBalancer/CloudShell.LoadBalancer.csproj",
            await GetFreePortAsync(),
            [
                ("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME", "true"),
                ("CLOUDSHELL_LOCAL_HOSTS_FILE", hostsFilePath)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:web");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:postgres");

        var loadBalancer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "load-balancer:public");
        var dnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:api");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local:name:app-cloudshell-local");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "dns:cloudshell-local:name:api-cloudshell-local");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:web");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:postgres");

        var apiAttributes = api.GetProperty("attributes");
        var loadBalancerAttributes = loadBalancer.GetProperty("attributes");
        Assert.Equal("application.container-app", api.GetProperty("typeId").GetString());
        Assert.Equal("traefik/whoami:v1.10", apiAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", apiAttributes.GetProperty("container.replicas").GetString());
        Assert.Contains(
            "docker:sample-host",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("cloudshell.loadBalancer", loadBalancer.GetProperty("typeId").GetString());
        Assert.Equal("traefik", loadBalancerAttributes.GetProperty("loadBalancer.provider").GetString());
        Assert.Equal("docker:sample-host", loadBalancerAttributes.GetProperty("loadBalancer.hostResourceId").GetString());
        Assert.Equal("3", loadBalancerAttributes.GetProperty("loadBalancer.routes").GetString());
        Assert.Equal("2", loadBalancerAttributes.GetProperty("loadBalancer.routes.http").GetString());
        Assert.Equal("1", loadBalancerAttributes.GetProperty("loadBalancer.routes.tcp").GetString());
        Assert.Equal("cloudshell.dnsZone", dnsZone.GetProperty("typeId").GetString());

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
        Assert.Contains("url: \"http://cloudshell-application-container-app-web:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-container-app-api-replica-1:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-container-app-api-replica-2:80\"", config);
        Assert.Contains("url: \"http://cloudshell-application-container-app-api-replica-3:80\"", config);
        Assert.Contains("HostSNI(`*`)", config);
        Assert.Contains("address: \"cloudshell-application-container-app-postgres:5432\"", config);

        var dnsReconcileAction = dnsZone
            .GetProperty("resourceActions")
            .GetProperty("reconcileNameMappings");
        var dnsReconcileHref = dnsReconcileAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The DNS zone reconcile action did not include an href.");
        var dnsReconcileJson = await host.SendAsync(HttpMethod.Post, dnsReconcileHref);
        using var dnsReconcileDocument = JsonDocument.Parse(dnsReconcileJson);
        var dnsReconcileMessage =
            dnsReconcileDocument.RootElement.GetProperty("message").GetString();
        Assert.Contains(
            "Published 2 local host name mapping(s)",
            dnsReconcileMessage);

        var hostsFile = await File.ReadAllTextAsync(hostsFilePath);
        Assert.Contains("127.0.0.1 app.cloudshell.local", hostsFile);
        Assert.Contains("127.0.0.1 api.cloudshell.local", hostsFile);
    }

    private static bool ReadBooleanConfigurationValue(string appSettingsPath, string configurationPath)
    {
        var fullPath = Path.Combine(SampleProcess.FindRepositoryRoot(), appSettingsPath);
        using var document = JsonDocument.Parse(File.ReadAllText(fullPath));
        var current = document.RootElement;
        foreach (var segment in configurationPath.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            if (!current.TryGetProperty(segment, out var next))
            {
                throw new InvalidOperationException(
                    $"Configuration path '{configurationPath}' was not found in '{appSettingsPath}'.");
            }

            current = next;
        }

        return current.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidOperationException(
                $"Configuration path '{configurationPath}' in '{appSettingsPath}' is not a boolean value.")
        };
    }

    private static async Task<IReadOnlyList<(string Key, string Value)>> CreateSampleHostLaunchEnvironmentAsync(
        string projectPath,
        int hostPort)
    {
        List<(string Key, string Value)> environment = [];
        var sampleName = GetSwitchReadinessSampleName(projectPath);

        if (sampleName == "ApplicationTopology")
        {
            environment.Add(("ApplicationTopology__ApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__FrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__GraphApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__GraphFrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__GraphConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__GraphSecretsServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__SqlServer__Port", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
        }
        else if (sampleName == "CloudShell.ContainerHost")
        {
            environment.Add(("ContainerHost__SqlServer__Port", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
        }
        else if (sampleName == "ContainerAppDeployment")
        {
            environment.Add(("ContainerAppDeployment__RegistryPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
        }
        else if (sampleName == "HostVirtualNetwork")
        {
            environment.Add(("HostVirtualNetwork__TargetPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("HostVirtualNetwork__VirtualNetworkPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
        }
        else if (sampleName == "LoadBalancer")
        {
            environment.Add(("CLOUDSHELL_LOADBALANCER_SKIP_TRAEFIK_RUNTIME", "true"));
            environment.Add((
                "CLOUDSHELL_LOCAL_HOSTS_FILE",
                Path.Combine(
                    Path.GetTempPath(),
                    $"cloudshell-load-balancer-{Guid.NewGuid():N}.hosts")));
        }
        else if (sampleName == "ProjectReference")
        {
            environment.Add(("ProjectReference__FrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ProjectReference__GraphApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ProjectReference__GraphFrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "ReplicatedContainerHealth")
        {
            environment.Add(("ReplicatedContainerHealth__ApiPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("ReplicatedContainerHealth__GraphOnlyStatusCacheMilliseconds", "25"));
        }
        else if (sampleName == "SettingsAndSecrets")
        {
            environment.Add(("Samples__SettingsAndSecrets__ConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__SettingsAndSecrets__SecretsServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__SettingsAndSecrets__ApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "SplitHosting")
        {
            environment.Add(("Authentication__BuiltInAuthority__Issuer", $"http://localhost:{hostPort}"));
        }
        else if (sampleName == "ThirdPartyIdentity")
        {
            environment.Add(("Authentication__Enabled", "false"));
            environment.Add(("Authentication__OpenIdConnect__RequireHttpsMetadata", "false"));
            environment.Add(("Keycloak__AdminBaseAddress", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__ThirdPartyIdentity__ApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__ThirdPartyIdentity__ConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }

        return environment;
    }

    private static string GetSwitchReadinessSampleName(string projectPath)
    {
        if (projectPath.Contains("/ApplicationTopology/", StringComparison.OrdinalIgnoreCase))
        {
            return "ApplicationTopology";
        }

        if (projectPath.Contains("/CloudShell.ContainerHost/", StringComparison.OrdinalIgnoreCase))
        {
            return "CloudShell.ContainerHost";
        }

        if (projectPath.Contains("/ContainerAppDeployment/", StringComparison.OrdinalIgnoreCase))
        {
            return "ContainerAppDeployment";
        }

        if (projectPath.Contains("/HostVirtualNetwork/", StringComparison.OrdinalIgnoreCase))
        {
            return "HostVirtualNetwork";
        }

        if (projectPath.Contains("/LoadBalancer/", StringComparison.OrdinalIgnoreCase))
        {
            return "LoadBalancer";
        }

        if (projectPath.Contains("/ProjectReference/", StringComparison.OrdinalIgnoreCase))
        {
            return "ProjectReference";
        }

        if (projectPath.Contains("/ReplicatedContainerHealth/", StringComparison.OrdinalIgnoreCase))
        {
            return "ReplicatedContainerHealth";
        }

        if (projectPath.Contains("/SettingsAndSecrets/", StringComparison.OrdinalIgnoreCase))
        {
            return "SettingsAndSecrets";
        }

        if (projectPath.Contains("/SplitHosting/ControlPlane/", StringComparison.OrdinalIgnoreCase))
        {
            return "SplitHosting";
        }

        if (projectPath.Contains("/ThirdPartyIdentity/", StringComparison.OrdinalIgnoreCase))
        {
            return "ThirdPartyIdentity";
        }

        throw new InvalidOperationException($"Could not resolve switch-readiness sample name for '{projectPath}'.");
    }

    private static void AssertNoUnexpectedLegacyResources(
        string projectPath,
        IReadOnlySet<string> resourceIds)
    {
        foreach (var legacyResourceId in GetUnexpectedLegacyResourceIds(projectPath))
        {
            Assert.DoesNotContain(legacyResourceId, resourceIds);
        }
    }

    private static IReadOnlyList<string> GetUnexpectedLegacyResourceIds(string projectPath)
    {
        return GetSwitchReadinessSampleName(projectPath) switch
        {
            "ApplicationTopology" =>
            [
                "application:application-topology-sql-server",
                "application:application-topology-sql-server/database:application-topology",
                "configuration:application-topology",
                "secrets-vault:application-topology",
                "application:application-topology-api",
                "application:application-topology-frontend"
            ],
            "CloudShell.ContainerHost" =>
            [
                "storage:local",
                "volume:sql-data",
                "application:sql-server"
            ],
            "ContainerAppDeployment" =>
            [
                "docker:container:sample-registry",
                "application:sample-api"
            ],
            "HostVirtualNetwork" =>
            [
                "application:vnet-api"
            ],
            "LoadBalancer" =>
            [
                "application:web",
                "application:api",
                "application:postgres"
            ],
            "ProjectReference" =>
            [
                "application:project-reference-api",
                "application:project-reference-frontend"
            ],
            "ReplicatedContainerHealth" =>
            [
                "application:api",
                "docker:sample"
            ],
            "SettingsAndSecrets" =>
            [
                "configuration:sample-app",
                "secrets-vault:sample-app",
                "application:settings-secrets-api"
            ],
            "SplitHosting" => [],
            "ThirdPartyIdentity" =>
            [
                "identity-provisioning:keycloak",
                "configuration:third-party-identity",
                "application:keycloak-provisioned-api"
            ],
            _ => []
        };
    }

    private static async Task CleanupSwitchReadinessRuntimeArtifactsAsync(string projectPath)
    {
        var sampleName = GetSwitchReadinessSampleName(projectPath);

        if (sampleName == "CloudShell.ContainerHost")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                ContainerHostSqlServerDockerBridge.SqlServerContainerName);
        }
        else if (sampleName == "ContainerAppDeployment")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                ContainerAppDeploymentDockerContainerRuntimeHandler.RegistryContainerName);
        }
        else if (sampleName == "ApplicationTopology")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                ApplicationTopologyGraphSqlServerDockerBridge.GraphSqlServerContainerName);
        }
        else if (sampleName == "LoadBalancer")
        {
            foreach (var path in Directory.EnumerateFiles(
                Path.GetTempPath(),
                "cloudshell-load-balancer-*.hosts"))
            {
                try
                {
                    File.Delete(path);
                }
                catch
                {
                    // Test cleanup should not hide the original test failure.
                }
            }
        }
        else if (sampleName == "ReplicatedContainerHealth")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                "cloudshell-replicated-health-graph-api-ingress");

            for (var replica = 1; replica <= 10; replica++)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(
                    $"cloudshell-replicated-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
            }
        }
        else if (sampleName == "ThirdPartyIdentity")
        {
            await CleanupThirdPartyIdentityKeycloakStacksAsync();
        }
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
            Timeout = TimeSpan.FromSeconds(10)
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

    private static async Task<(DockerComposeStack Stack, int Port)> StartThirdPartyIdentityKeycloakAsync(
        string root,
        string projectNamePrefix)
    {
        var portBindFailures = new List<Exception>();
        for (var attempt = 1; attempt <= 3; attempt++)
        {
            var keycloakPort = await GetFreePortAsync();
            var projectName = $"{projectNamePrefix}-{Guid.NewGuid():N}";
            try
            {
                var stack = await DockerComposeStack.StartAsync(
                    root,
                    "samples/ThirdPartyIdentity/docker-compose.yml",
                    projectName,
                    [("KEYCLOAK_PORT", keycloakPort.ToString(CultureInfo.InvariantCulture))]);
                return (stack, keycloakPort);
            }
            catch (InvalidOperationException exception) when (IsDockerPortBindFailure(exception))
            {
                portBindFailures.Add(exception);
            }
        }

        throw new InvalidOperationException(
            "Could not start the ThirdPartyIdentity Keycloak compose stack after retrying random host ports.",
            new AggregateException(portBindFailures));
    }

    private static bool IsDockerPortBindFailure(Exception exception)
    {
        return exception.Message.Contains("port is already allocated", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("Ports are not available", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("failed to bind", StringComparison.OrdinalIgnoreCase) ||
            exception.Message.Contains("address already in use", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task CleanupThirdPartyIdentityKeycloakStacksAsync()
    {
        await DockerComposeStack.RemoveProjectsByPrefixAsync("cloudshell-third-party-identity-test-");
        await DockerComposeStack.RemoveProjectsByPrefixAsync("cloudshell-third-party-identity-graph-test-");
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

    private static async Task<string> WaitForLogSourceAsync(
        SampleProcess host,
        string resourceId)
    {
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        string? lastBody = null;
        do
        {
            lastBody = await host.GetStringAsync(
                $"/api/control-plane/v1/log-sources?resourceId={Uri.EscapeDataString(resourceId)}");
            using var document = JsonDocument.Parse(lastBody);
            foreach (var source in document.RootElement.EnumerateArray())
            {
                if (string.Equals(
                        source.GetProperty("resourceId").GetString(),
                        resourceId,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return source.GetProperty("id").GetString() ??
                        throw new InvalidOperationException("The log source did not include an id.");
                }
            }

            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException(
            $"Timed out waiting for log source for resource '{resourceId}'. Last response: {lastBody}");
    }

    private static async Task<IReadOnlyList<string>> WaitForLogEntriesAsync(
        SampleProcess host,
        string logSourceId,
        Func<string, bool>? containsEntry = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        string? lastBody = null;
        do
        {
            lastBody = await host.GetStringAsync(
                $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(logSourceId)}/entries?maxEntries=50");
            using var document = JsonDocument.Parse(lastBody);
            var entries = document.RootElement
                .EnumerateArray()
                .Select(entry => entry.GetProperty("message").GetString() ?? string.Empty)
                .Where(message => !string.IsNullOrWhiteSpace(message))
                .ToArray();
            if (entries.Length > 0 &&
                (containsEntry is null || entries.Any(containsEntry)))
            {
                return entries;
            }

            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException(
            $"Timed out waiting for log entries for source '{logSourceId}'. Last response: {lastBody}");
    }

    private static async Task<IReadOnlyList<string>> WaitForAnyGraphReplicaLogEntriesAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas,
        Func<string, bool> containsEntry)
    {
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        var lastResponses = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        do
        {
            for (var replica = 1; replica <= expectedReplicas; replica++)
            {
                var logSourceId = $"{resourceId}:replica-{replica.ToString(CultureInfo.InvariantCulture)}:logs";
                var body = await host.GetStringAsync(
                    $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(logSourceId)}/entries?maxEntries=50");
                lastResponses[logSourceId] = body;
                using var document = JsonDocument.Parse(body);
                var entries = document.RootElement
                    .EnumerateArray()
                    .Select(entry => entry.GetProperty("message").GetString() ?? string.Empty)
                    .Where(message => !string.IsNullOrWhiteSpace(message))
                    .ToArray();
                if (entries.Length > 0 && entries.Any(containsEntry))
                {
                    return entries;
                }
            }

            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        var lastResponseText = string.Join(
            Environment.NewLine,
            lastResponses.Select(response => $"{response.Key}: {response.Value}"));
        throw new TimeoutException(
            $"Timed out waiting for graph replica log entries for resource '{resourceId}'. Last responses: {lastResponseText}");
    }

    private static bool HasResourceState(JsonElement resource, ResourceState expected)
    {
        if (!resource.TryGetProperty("state", out var state) ||
            state.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return false;
        }

        return state.ValueKind switch
        {
            JsonValueKind.String => string.Equals(
                state.GetString(),
                expected.ToString(),
                StringComparison.OrdinalIgnoreCase),
            JsonValueKind.Number => state.TryGetInt32(out var value) &&
                value == (int)expected,
            _ => false
        };
    }

    private static async Task<JsonElement> WaitForResourceStateAsync(
        SampleProcess host,
        string resourceId,
        ResourceState state,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? lastBody = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastBody = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var document = JsonDocument.Parse(lastBody);
            var resource = document.RootElement
                .EnumerateArray()
                .FirstOrDefault(resource =>
                    string.Equals(
                        resource.GetProperty("id").GetString(),
                        resourceId,
                        StringComparison.OrdinalIgnoreCase));

            if (resource.ValueKind != JsonValueKind.Undefined &&
                HasResourceState(resource, state))
            {
                return resource.Clone();
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Resource '{resourceId}' did not reach state '{state}' within {timeout}." +
            $"{Environment.NewLine}{lastBody}");
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

    private static async Task<IReadOnlyList<JsonElement>> WaitForTraceSpansByResourceAsync(
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
                    $"/api/control-plane/v1/traces?resourceId={Uri.EscapeDataString(resourceId)}&maxSpans=50");
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
            $"Traces for resource '{resourceId}' were not ingested within {timeout}." +
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

    private static async Task<IReadOnlyDictionary<string, string>> GetDockerContainerIdsAsync(
        IReadOnlyCollection<string> containerNames)
    {
        var containerIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var containerName in containerNames)
        {
            var containerId = await DockerComposeStack.GetContainerIdAsync(containerName);
            if (string.IsNullOrWhiteSpace(containerId))
            {
                throw new InvalidOperationException(
                    $"Docker container '{containerName}' did not have an inspectable id.");
            }

            containerIds[containerName] = containerId;
        }

        return containerIds;
    }

    private static async Task<bool> WaitForDockerContainerIdChangedAsync(
        string containerName,
        string previousContainerId,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            var currentContainerId = await DockerComposeStack.GetContainerIdAsync(containerName);
            if (!string.IsNullOrWhiteSpace(currentContainerId) &&
                !string.Equals(currentContainerId, previousContainerId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            await Task.Delay(250);
        }

        return false;
    }

    private static async Task<bool> AnyDockerContainerExistsAsync(IReadOnlyCollection<string> containerNames)
    {
        foreach (var containerName in containerNames)
        {
            if (await DockerComposeStack.ContainerExistsAsync(containerName))
            {
                return true;
            }
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

    private static async Task<bool> WaitForDockerComposeProjectRemovedAsync(string projectName, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (!await DockerComposeStack.ProjectExistsAsync(projectName))
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

    private static async Task AssertGraphResourceHealthChecksHealthyAsync(
        SampleProcess host,
        string resourceId,
        int endpointPort,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? lastSummaryJson = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastSummaryJson = await host.SendAsync(
                HttpMethod.Post,
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/health/refresh");
            using var summaryDocument = JsonDocument.Parse(lastSummaryJson);
            var summary = summaryDocument.RootElement;
            var checks = summary.GetProperty("checks").EnumerateArray().ToArray();

            if (summary.GetProperty("resourceId").GetString() == resourceId &&
                summary.GetProperty("status").GetInt32() == (int)ResourceHealthStatus.Healthy &&
                checks.Any(check => IsHealthyHttpCheck(check, ResourceProbeType.Health, "health", endpointPort, "/health")) &&
                checks.Any(check => IsHealthyHttpCheck(check, ResourceProbeType.Liveness, "alive", endpointPort, "/alive")))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Graph resource '{resourceId}' health checks did not become healthy within {timeout}." +
            $"{Environment.NewLine}{lastSummaryJson}");
    }

    private static bool IsHealthyHttpCheck(
        JsonElement check,
        ResourceProbeType probeType,
        string name,
        int endpointPort,
        string path)
    {
        if (check.GetProperty("status").GetInt32() != (int)ResourceHealthStatus.Healthy ||
            check.GetProperty("outcome").GetInt32() != (int)ResourceHealthCheckOutcome.Responded)
        {
            return false;
        }

        var definition = check.GetProperty("check");
        if (definition.GetProperty("type").GetInt32() != (int)probeType ||
            !string.Equals(definition.GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var uri = check.GetProperty("uri").GetString();
        return uri is not null &&
            uri.StartsWith($"http://localhost:{endpointPort.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase) &&
            uri.EndsWith(path, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task AssertGraphResourceRuntimeHealthAggregatesAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas,
        TimeSpan? timeout = null)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout ?? TimeSpan.FromSeconds(30));
        string? lastSummariesJson = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastSummariesJson = await host.SendAsync(
                HttpMethod.Post,
                "/api/control-plane/v1/resource-health/refresh");
            using var summariesDocument = JsonDocument.Parse(lastSummariesJson);
            if (!summariesDocument.RootElement.TryGetProperty(resourceId, out var summary))
            {
                await Task.Delay(250);
                continue;
            }

            var checks = summary.GetProperty("checks").EnumerateArray().ToArray();
            if (summary.GetProperty("resourceId").GetString() == resourceId &&
                summary.GetProperty("status").GetInt32() == (int)ResourceHealthStatus.Healthy &&
                checks.Any(check => HasRuntimeReplicaObservations(check, ResourceProbeType.Health, "health", expectedReplicas)) &&
                checks.Any(check => HasRuntimeReplicaObservations(check, ResourceProbeType.Liveness, "alive", expectedReplicas)))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Graph resource '{resourceId}' runtime-scope health did not aggregate as healthy within {timeout ?? TimeSpan.FromSeconds(30)}." +
            $"{Environment.NewLine}{lastSummariesJson}");
    }

    private static bool HasRuntimeReplicaObservations(
        JsonElement check,
        ResourceProbeType probeType,
        string name,
        int expectedReplicas)
    {
        if (check.GetProperty("check").GetProperty("type").GetInt32() != (int)probeType ||
            !string.Equals(check.GetProperty("check").GetProperty("name").GetString(), name, StringComparison.OrdinalIgnoreCase) ||
            check.GetProperty("status").GetInt32() != (int)ResourceHealthStatus.Healthy)
        {
            return false;
        }

        var observations = check.GetProperty("observations").EnumerateArray().ToArray();
        if (observations.Length != expectedReplicas)
        {
            return false;
        }

        for (var replica = 1; replica <= expectedReplicas; replica++)
        {
            var replicaOrdinal = replica.ToString(CultureInfo.InvariantCulture);
            if (!observations.Any(observation =>
                    string.Equals(observation.GetProperty("scopeKind").GetString(), "runtime", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        observation.GetProperty("resourceId").GetString(),
                        ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica),
                        StringComparison.OrdinalIgnoreCase) &&
                    observation.GetProperty("attributes").GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString() == replicaOrdinal))
            {
                return false;
            }
        }

        return true;
    }

    private static async Task AssertGraphReplicaLogSourcesAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas)
    {
        var logSourcesJson = await host.GetStringAsync(
            $"/api/control-plane/v1/log-sources?resourceId={Uri.EscapeDataString(resourceId)}");
        using var logSourcesDocument = JsonDocument.Parse(logSourcesJson);
        var sources = logSourcesDocument.RootElement
            .EnumerateArray()
            .Where(source =>
                source.TryGetProperty("producerResourceId", out var producer) &&
                string.Equals(producer.GetString(), resourceId, StringComparison.OrdinalIgnoreCase))
            .OrderBy(source => source.GetProperty("name").GetString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedReplicas, sources.Length);
        for (var replica = 1; replica <= expectedReplicas; replica++)
        {
            var source = sources[replica - 1];
            Assert.Equal(
                $"{resourceId}:replica-{replica.ToString(CultureInfo.InvariantCulture)}:logs",
                source.GetProperty("id").GetString());
            Assert.Equal(
                $"Replica {replica.ToString(CultureInfo.InvariantCulture)} logs",
                source.GetProperty("name").GetString());
            Assert.Equal(resourceId, source.GetProperty("resourceId").GetString());
            Assert.Equal((int)LogSourceKind.Resource, source.GetProperty("sourceKind").GetInt32());
            Assert.Equal((int)ResourceLogSourceKind.Container, source.GetProperty("kind").GetInt32());
            Assert.Equal((int)LogFormat.JsonConsole, source.GetProperty("format").GetInt32());
            Assert.Equal((int)ResourceLogSourceOrigin.ProviderProjected, source.GetProperty("origin").GetInt32());
            Assert.Equal((int)LogSourceAvailability.ProducerRunning, source.GetProperty("availability").GetInt32());
        }
    }

    private static async Task AssertGraphReplicaRuntimeEnvironmentAsync(
        string containerName,
        int replica,
        int replicaCount = 3)
    {
        var replicaResourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var environment = await DockerComposeStack.GetContainerEnvironmentAsync(containerName);
        Assert.Contains($"CLOUDSHELL_RESOURCE_ID={replicaResourceId}", environment);
        Assert.Contains($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}", environment);
        Assert.Contains(
            $"OTEL_SERVICE_NAME=replicated-container-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
            environment);
        Assert.Contains(
            environment,
            variable => variable.StartsWith("CLOUDSHELL_TRACE_INGEST_ENDPOINT=http://host.docker.internal:", StringComparison.Ordinal));
        Assert.Contains(
            environment,
            variable => variable.StartsWith("CLOUDSHELL_METRIC_INGEST_ENDPOINT=http://host.docker.internal:", StringComparison.Ordinal));
        var resourceAttributes = Assert.Single(
            environment,
            variable => variable.StartsWith("OTEL_RESOURCE_ATTRIBUTES=", StringComparison.Ordinal));
        Assert.Contains($"cloudshell.resource.id={replicaResourceId}", resourceAttributes);
        Assert.Contains($"telemetry.scope.resourceId={ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId}", resourceAttributes);
        Assert.Contains($"telemetry.scope.name=Replica {replica.ToString(CultureInfo.InvariantCulture)}", resourceAttributes);
        Assert.Contains("telemetry.scope.kind=runtime", resourceAttributes);
        Assert.Contains($"runtime.replica.ordinal={replica.ToString(CultureInfo.InvariantCulture)}", resourceAttributes);
        Assert.Contains($"runtime.replica.count={replicaCount.ToString(CultureInfo.InvariantCulture)}", resourceAttributes);
    }

    private static async Task AssertGraphReplicaTelemetryAsync(
        SampleProcess host,
        int replica,
        TimeSpan timeout)
    {
        var replicaResourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var metrics = await WaitForMetricPointsAsync(
            host,
            replicaResourceId,
            timeout,
            points => points.Any(point =>
                point.GetProperty("name").GetString() == "http.server.requests" &&
                point.GetProperty("resourceId").GetString() == replicaResourceId &&
                point.TryGetProperty("attributes", out var attributes) &&
                attributes.TryGetProperty("telemetry.scope.resourceId", out var scopeResourceId) &&
                scopeResourceId.GetString() == ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId));
        Assert.NotEmpty(metrics);
    }

    private static async Task AssertAnyGraphReplicaWorkTraceAsync(
        SampleProcess host,
        int expectedReplicas,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var lastErrors = new List<string>();
        do
        {
            lastErrors.Clear();
            for (var replica = 1; replica <= expectedReplicas; replica++)
            {
                var replicaResourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
                try
                {
                    var spans = await WaitForTraceSpansByResourceAsync(
                        host,
                        replicaResourceId,
                        TimeSpan.FromMilliseconds(250),
                        spans => spans.Any(span => IsGraphReplicaWorkSpan(span, replicaResourceId, replica)));
                    Assert.NotEmpty(spans);
                    return;
                }
                catch (TimeoutException exception)
                {
                    lastErrors.Add(exception.Message);
                }
            }
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException(
            $"Graph replica work trace was not ingested by any of {expectedReplicas.ToString(CultureInfo.InvariantCulture)} replica(s)." +
            $"{Environment.NewLine}{string.Join(Environment.NewLine, lastErrors)}");
    }

    private static bool IsGraphReplicaWorkSpan(
        JsonElement span,
        string replicaResourceId,
        int replica)
    {
        if (span.GetProperty("name").GetString() != "Handle demo work" ||
            span.GetProperty("resourceId").GetString() != replicaResourceId ||
            !span.TryGetProperty("spanAttributes", out var attributes) ||
            !attributes.TryGetProperty("telemetry.scope.resourceId", out var scopeResourceId) ||
            !attributes.TryGetProperty("runtime.replica.ordinal", out var replicaOrdinal))
        {
            return false;
        }

        return scopeResourceId.GetString() == ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId &&
            replicaOrdinal.GetString() == replica.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task AssertGraphReplicaResourceObservabilityAsync(
        SampleProcess host,
        int replica)
    {
        var replicaResourceId = ReplicatedContainerHealthGraphOnlyRuntimeConventions.CreateReplicaResourceId(replica);
        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var runtimeReplica = Assert.Single(
            resourcesDocument.RootElement.EnumerateArray(),
            resource => string.Equals(
                resource.GetProperty("id").GetString(),
                replicaResourceId,
                StringComparison.OrdinalIgnoreCase));
        Assert.Equal((int)ResourceVisibility.Hidden, runtimeReplica.GetProperty("visibility").GetInt32());

        var observability = runtimeReplica.GetProperty("observability");
        Assert.True(observability.GetProperty("logs").GetBoolean());
        Assert.True(observability.GetProperty("traces").GetBoolean());
        Assert.True(observability.GetProperty("metrics").GetBoolean());
        Assert.Equal(
            $"replicated-container-health-graph-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
            observability.GetProperty("serviceName").GetString());

        var attributes = observability.GetProperty("attributes");
        Assert.Equal(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
            attributes.GetProperty("telemetry.scope.resourceId").GetString());
        Assert.Equal(
            replica.ToString(CultureInfo.InvariantCulture),
            attributes.GetProperty("runtime.replica.ordinal").GetString());

        var scope = Assert.Single(observability.GetProperty("scopes").EnumerateArray());
        Assert.Equal(
            ReplicatedContainerHealthGraphOnlyRuntimeConventions.GraphApiResourceId,
            scope.GetProperty("scopeResourceId").GetString());
        Assert.Equal($"Replica {replica.ToString(CultureInfo.InvariantCulture)}", scope.GetProperty("name").GetString());
        Assert.Equal("runtime", scope.GetProperty("kind").GetString());
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

        public string ProjectName => projectName;

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

        public static async Task<string?> GetContainerIdAsync(string containerName)
        {
            try
            {
                var result = await RunDockerAsync(
                    SampleProcess.FindRepositoryRoot(),
                    ["container", "inspect", "--format", "{{.Id}}", containerName],
                    null,
                    TimeSpan.FromSeconds(10),
                    throwOnError: false);
                return result.ExitCode == 0
                    ? result.Output.Trim()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        public static async Task<IReadOnlyList<string>> GetContainerEnvironmentAsync(string containerName)
        {
            var result = await RunDockerAsync(
                SampleProcess.FindRepositoryRoot(),
                ["container", "inspect", "--format", "{{json .Config.Env}}", containerName],
                null,
                TimeSpan.FromSeconds(10),
                throwOnError: true);

            return JsonSerializer.Deserialize<IReadOnlyList<string>>(result.Output.Trim())
                ?? [];
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

        public static async Task RemoveProjectsByPrefixAsync(string projectNamePrefix)
        {
            var projects = await ListComposeProjectsByPrefixAsync(projectNamePrefix);
            foreach (var project in projects)
            {
                await RemoveProjectArtifactsAsync(SampleProcess.FindRepositoryRoot(), project);
            }
        }

        public static async Task<bool> ProjectExistsAsync(string projectName)
        {
            var containers = await RunDockerAsync(
                SampleProcess.FindRepositoryRoot(),
                ["ps", "-a", "--filter", $"label=com.docker.compose.project={projectName}", "--format", "{{.ID}}"],
                null,
                TimeSpan.FromSeconds(15),
                throwOnError: false);
            var containerIds = containers.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (containerIds.Length > 0)
            {
                return true;
            }

            var networks = await RunDockerAsync(
                SampleProcess.FindRepositoryRoot(),
                ["network", "ls", "--filter", $"label=com.docker.compose.project={projectName}", "--format", "{{.ID}}"],
                null,
                TimeSpan.FromSeconds(15),
                throwOnError: false);
            var networkIds = networks.Output.Split(
                '\n',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return networkIds.Length > 0;
        }

        public static async Task<DockerComposeStack> StartAsync(
            string root,
            string composeFile,
            string projectName,
            IReadOnlyList<(string Key, string Value)> environment)
        {
            var stack = new DockerComposeStack(root, composeFile, projectName);
            try
            {
                await RunDockerAsync(
                    root,
                    ["compose", "-f", composeFile, "-p", projectName, "up", "-d"],
                    environment,
                    TimeSpan.FromMinutes(3),
                    throwOnError: true);
            }
            catch
            {
                stack.Dispose();
                throw;
            }

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

            try
            {
                RemoveProjectArtifactsAsync(root, projectName)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Test cleanup should not hide the original test failure.
            }
        }

        private static async Task<IReadOnlyList<string>> ListComposeProjectsByPrefixAsync(string projectNamePrefix)
        {
            var result = await RunDockerAsync(
                SampleProcess.FindRepositoryRoot(),
                ["ps", "-a", "--filter", "label=com.docker.compose.project", "--format", "{{.Label \"com.docker.compose.project\"}}"],
                null,
                TimeSpan.FromSeconds(15),
                throwOnError: false);
            if (result.ExitCode != 0)
            {
                return [];
            }

            return result.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(project => project.StartsWith(projectNamePrefix, StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static async Task RemoveProjectArtifactsAsync(string workingDirectory, string projectName)
        {
            await RemoveProjectContainersAsync(workingDirectory, projectName);
            await RemoveProjectNetworksAsync(workingDirectory, projectName);
        }

        private static async Task RemoveProjectContainersAsync(string workingDirectory, string projectName)
        {
            var containers = await RunDockerAsync(
                workingDirectory,
                ["ps", "-a", "--filter", $"label=com.docker.compose.project={projectName}", "--format", "{{.ID}}"],
                null,
                TimeSpan.FromSeconds(15),
                throwOnError: false);
            var containerIds = containers.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (containerIds.Length == 0)
            {
                return;
            }

            await RunDockerAsync(
                workingDirectory,
                ["rm", "-f", .. containerIds],
                null,
                TimeSpan.FromSeconds(30),
                throwOnError: false);
        }

        private static async Task RemoveProjectNetworksAsync(string workingDirectory, string projectName)
        {
            var networks = await RunDockerAsync(
                workingDirectory,
                ["network", "ls", "--filter", $"label=com.docker.compose.project={projectName}", "--format", "{{.ID}}"],
                null,
                TimeSpan.FromSeconds(15),
                throwOnError: false);
            var networkIds = networks.Output
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (networkIds.Length == 0)
            {
                return;
            }

            await RunDockerAsync(
                workingDirectory,
                ["network", "rm", .. networkIds],
                null,
                TimeSpan.FromSeconds(30),
                throwOnError: false);
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
            IReadOnlyList<(string Key, string Value)>? environment = null,
            bool bindToAnyAddress = false)
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
            var listenAddress = bindToAnyAddress
                ? new Uri($"http://0.0.0.0:{port}")
                : baseAddress;
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
            startInfo.ArgumentList.Add(listenAddress.ToString());
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
                Timeout = TimeSpan.FromSeconds(10)
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

            HttpResponseMessage response;
            try
            {
                response = await client.SendAsync(request);
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
            {
                throw new InvalidOperationException(
                    $"GET {path} failed before a response was received.{Environment.NewLine}{GetOutput()}",
                    exception);
            }

            using (response)
            {
                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    throw new HttpRequestException(
                        $"Response status code does not indicate success: {(int)response.StatusCode} ({response.ReasonPhrase}).{Environment.NewLine}{content}");
                }

                return content;
            }
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
