using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.Observability;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ApplicationTopologyHost;
using CloudShell.ApplicationTopology.ServiceDefaults;
using CloudShell.ContainerHost;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Platform;
using CloudShell.ControlPlane.Providers;
using CloudShell.ControlPlane.ResourceModel;
using CloudShell.DeviceRegistry.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Formatter;
using MQTTnet.Protocol;
using ResourceAttributeId = CloudShell.ResourceModel.ResourceAttributeId;
using ResourceAttributeValue = CloudShell.ResourceModel.ResourceAttributeValue;
using ResourceCapabilityId = CloudShell.ResourceModel.ResourceCapabilityId;
using ResourceDefinitionJson = CloudShell.ResourceModel.ResourceDefinitionJson;
using ResourceGraphState = CloudShell.ResourceModel.ResourceState;
using ResourceHealthCheckCapabilityIds = CloudShell.ResourceModel.ResourceHealthCheckCapabilityIds;
using ResourceReference = CloudShell.ResourceModel.ResourceReference;
using SqlServerResources = CloudShell.ControlPlane.Providers.SqlServerResourceDefaults;

namespace CloudShell.Sample.Tests;

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SampleSmokeCollection
    : ICollectionFixture<SampleSmokeRuntimeCleanupFixture>
{
    public const string Name = "Sample smoke tests";
}

public sealed class SampleSmokeRuntimeCleanupFixture : IAsyncLifetime
{
    private static readonly TimeSpan DockerCleanupTimeout = TimeSpan.FromSeconds(5);

    public async Task InitializeAsync() =>
        await CleanupAsync();

    public async Task DisposeAsync() =>
        await CleanupAsync();

    private static async Task CleanupAsync()
    {
        await RemoveContainerIfExistsAsync("cloudshell-replicated-health-api-ingress");
        for (var replica = 1; replica <= 10; replica++)
        {
            await RemoveContainerIfExistsAsync(
                $"cloudshell-replicated-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
        }

        var signalRDefinition = LocalDockerContainerApplicationRuntimeDefinition.CreateDefault(
            "application.container-app:signalr-api");
        await RemoveContainerIfExistsAsync("cloudshell-signalr-api-ingress");
        await RemoveContainerIfExistsAsync(
            LocalDockerContainerApplicationRuntimeConventions.CreateIngressContainerName(signalRDefinition));
        for (var replica = 1; replica <= 10; replica++)
        {
            await RemoveContainerIfExistsAsync(
                $"cloudshell-signalr-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
            await RemoveContainerIfExistsAsync(
                LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(signalRDefinition, replica));
        }

        await RemoveContainersMatchingAsync(containerName =>
            containerName.StartsWith("cloudshell-", StringComparison.OrdinalIgnoreCase) &&
            containerName.EndsWith("-rabbitmq-rabbitmq", StringComparison.OrdinalIgnoreCase));

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

    private static async Task RemoveContainersMatchingAsync(
        Func<string, bool> predicate)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("ps");
        startInfo.ArgumentList.Add("-a");
        startInfo.ArgumentList.Add("--format");
        startInfo.ArgumentList.Add("{{.Names}}");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(DockerCleanupTimeout);
                output.Append(await outputTask);
                output.Append(await errorTask);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
                }

                return;
            }

            foreach (var containerName in output
                .ToString()
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(predicate))
            {
                await RemoveContainerIfExistsAsync(containerName);
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            TimeoutException)
        {
            // Docker may be unavailable for non-Docker sample tests.
        }
    }

    private static async Task RemoveContainerIfExistsAsync(string containerName)
    {
        var output = new StringBuilder();
        var startInfo = new ProcessStartInfo("docker")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("rm");
        startInfo.ArgumentList.Add("-f");
        startInfo.ArgumentList.Add(containerName);

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return;
            }

            var outputTask = CaptureAsync(process.StandardOutput, output);
            var errorTask = CaptureAsync(process.StandardError, output);
            try
            {
                await process.WaitForExitAsync().WaitAsync(DockerCleanupTimeout);
                await Task.WhenAll(outputTask, errorTask);
            }
            catch (TimeoutException)
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(1));
                }
            }
        }
        catch (Exception exception) when (
            exception is InvalidOperationException or
            System.ComponentModel.Win32Exception or
            TimeoutException)
        {
            // Docker may be unavailable for non-Docker sample tests.
        }

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
}

[Collection(SampleSmokeCollection.Name)]
[Trait("Category", "Integration")]
public sealed class SampleSmokeTests
{
    // Sample host smoke tests are serialized because they launch processes,
    // reserve local ports, write sample data paths, and may create fixed-name
    // Docker resources. Recording-runner adapter tests can stay parallel.
    private static readonly TimeSpan StartupTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan SampleHostLaunchTimeout = TimeSpan.FromMinutes(3);

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
            "samples/ProjectReference/AppHost/CloudShell.ProjectReferenceAppHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/DeviceRegistry/AppHost/CloudShell.DeviceRegistryAppHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/RabbitMQMessaging/AppHost/CloudShell.RabbitMQMessagingAppHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/ReplicatedContainerHealth/CloudShell.ReplicatedContainerHealth.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/RoboticMowerIoT/AppHost/CloudShell.RoboticMowerIoTAppHost.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/SettingsAndSecrets/CloudShell.SettingsAndSecrets.csproj",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/PythonAppHost/AppHost/app_host.py",
            resourceHostPaths
        };
        yield return new object[]
        {
            "samples/ReactTypeScriptApp/AppHost/package.json",
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

    public static IEnumerable<object[]> CSharpLauncherTemplateSampleProjects()
    {
        yield return new object[]
        {
            "samples/JavaScriptApp/AppHost/CloudShell.JavaScriptAppHost.csproj",
            new[]
            {
                "configuration.store:javascript-app-settings",
                "application.javascript-app:javascript-frontend"
            }
        };
        yield return new object[]
        {
            "samples/CSharpAppHost/AppHost/CloudShell.CSharpAppHost.csproj",
            new[]
            {
                "configuration.store:csharp-app-settings",
                "secrets.vault:csharp-app-secrets",
                "application.javascript-app:csharp-declared-frontend"
            }
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
        var host = IsLauncherSampleProject(projectPath)
            ? await SampleProcess.StartLauncherAsync(
                projectPath,
                port,
                await CreateSampleHostLaunchEnvironmentAsync(projectPath, port))
            : await SampleProcess.StartAsync(
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

    [Theory]
    [MemberData(nameof(CSharpLauncherTemplateSampleProjects))]
    public async Task CSharpLauncherSamples_EmitTemplatesThatApplyInMemory(
        string projectPath,
        string[] expectedResourceIds)
    {
        var document = await SampleProcess.RunCSharpLauncherTemplateAsync(projectPath);
        var template = CloudShell.ResourceModel.ResourceTemplateSerializer.DeserializeTemplate(document);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddBuiltInResourceModelProviderTypes();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyTemplateAsync(
                template,
                new CloudShell.ResourceModel.ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 7, 13, 12, 30, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, FormatDiagnostics(result.Diagnostics));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<CloudShell.ResourceModel.ResourceGraphModel>()
            .GetSnapshotAsync();
        var resourceIds = snapshot.Resources
            .Select(resource => resource.EffectiveResourceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var expectedResourceId in expectedResourceIds)
        {
            Assert.Contains(expectedResourceId, resourceIds);
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
            .AddBuiltInProviderResourceManagerProjections()
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
        const string sqlContainerName = "cloudshell-container-host-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.ContainerImage) ||
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
        const string sqlContainerName = "cloudshell-container-host-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.ContainerImage))
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
    public async Task ProjectReferenceLauncher_RunsProjectsWithoutOldProviderRecords()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var apiEndpoint = $"http://127.0.0.1:{apiPort}";
        var frontendEndpoint = $"http://127.0.0.1:{frontendPort}";
        const string apiResourceId = "application.dotnet-app:project-reference-api";
        const string frontendResourceId = "application.dotnet-app:project-reference-frontend";
        using var host = await SampleProcess.StartLauncherAsync(
            "samples/ProjectReference/AppHost/CloudShell.ProjectReferenceAppHost.csproj",
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
    public async Task ProjectReferenceLauncher_HonorsResourceManagerReadOnlySetting()
    {
        using var host = await SampleProcess.StartLauncherAsync(
            "samples/ProjectReference/AppHost/CloudShell.ProjectReferenceAppHost.csproj",
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
            $"/resources/{Uri.EscapeDataString("application.dotnet-app:project-reference-api")}/details");
        Assert.Contains("Stop unavailable. Resource Manager is in read-only mode.", resourceDetailsHtml);

        var addResourceHtml = await host.GetStringAsync("/resources/add");
        Assert.Contains("Resource registration is disabled", addResourceHtml);
        Assert.DoesNotContain("Create a resource group", addResourceHtml);
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
    public async Task ApplicationTopologyHost_GraphBackingServicesRunThroughResourceModelRuntime()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
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
                ("ApplicationTopology__ConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__SecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture))
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphSettings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:application-topology-settings");
        var graphSecrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:application-topology-secrets");
        var graphApi = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:application-topology-api");

        var graphSettingsEndpoint = GetEndpointAddress(graphSettings, "settings");
        var graphSecretsEndpointAddress = GetEndpointAddress(graphSecrets, "secrets");
        Assert.StartsWith(
            graphConfigurationEndpoint,
            graphSettingsEndpoint,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"/api/configuration/stores/{Uri.EscapeDataString("configuration.store:application-topology-settings")}/settings",
            graphSettingsEndpoint,
            StringComparison.Ordinal);
        Assert.StartsWith(
            graphSecretsEndpoint,
            graphSecretsEndpointAddress,
            StringComparison.Ordinal);
        Assert.EndsWith(
            $"/api/secrets/vaults/{Uri.EscapeDataString("secrets.vault:application-topology-secrets")}/secrets",
            graphSecretsEndpointAddress,
            StringComparison.Ordinal);
        await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology settings");
        await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphConfigurationEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphSecretsEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);

        var graphResourceToken = await host.GetClientCredentialsTokenAsync(
            "application.dotnet-app:application-topology-api/application-topology-api",
            "local-development-application-topology-api-secret",
            "ControlPlane.Access");
        var graphSettingsJson = await host.GetAbsoluteStringAsync(
            graphSettingsEndpoint,
            graphResourceToken);
        using var graphSettingsDocument = JsonDocument.Parse(graphSettingsJson);
        Assert.Contains(
            graphSettingsDocument.RootElement.EnumerateArray(),
            setting =>
                setting.GetProperty("name").GetString() == "ApplicationTopology:Message" &&
                setting.GetProperty("value").GetString() == "Hello from CloudShell resource configuration.");
        Assert.Contains(
            graphSettingsDocument.RootElement.EnumerateArray(),
            setting =>
                setting.GetProperty("name").GetString() == "ApplicationTopology:Mode" &&
                setting.GetProperty("value").GetString() == "Resource model");

        var graphSecretJson = await host.GetAbsoluteStringAsync(
            $"{graphSecretsEndpointAddress.TrimEnd('/')}/ApplicationTopology--ExternalApiKey",
            graphResourceToken);
        using var graphSecretDocument = JsonDocument.Parse(graphSecretJson);
        Assert.Equal(
            "local-development-application-topology-api-key",
            graphSecretDocument.RootElement.GetProperty("value").GetString());

        await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{apiPort}/health",
            bearerToken: null,
            StartupTimeout);
        var graphApiSettingsJson = await host.GetAbsoluteStringAsync(
            $"http://localhost:{apiPort}/settings");
        using var graphApiSettingsDocument = JsonDocument.Parse(graphApiSettingsJson);
        var graphApiSettings = graphApiSettingsDocument.RootElement;
        Assert.Equal("Hello from CloudShell resource configuration.", graphApiSettings.GetProperty("message").GetString());
        Assert.Equal("Resource model", graphApiSettings.GetProperty("mode").GetString());
        Assert.True(graphApiSettings.GetProperty("externalApiKeyConfigured").GetBoolean());
    }

    [Fact]
    public async Task ApplicationTopologyHost_DeclaresWorkloadThroughResourceModel()
    {
        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
        var graphConfigurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var graphSecretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var sqlPort = await GetFreePortAsync();
        var hostsFilePath = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-application-topology-hosts-{Guid.NewGuid():N}");
        var configurationServiceBasePort = await GetServiceBasePortAsync("configuration:application-topology");
        var secretsServiceBasePort = await GetServiceBasePortAsync("secrets-vault:application-topology");
        using var host = await SampleProcess.StartAsync(
            "samples/ApplicationTopology/Host/CloudShell.ApplicationTopologyHost.csproj",
            await GetFreePortAsync(),
            [
                ("ApplicationTopology__ApiEndpoint", $"http://localhost:{apiPort}"),
                ("ApplicationTopology__FrontendEndpoint", $"http://localhost:{frontendPort}"),
                ("ApplicationTopology__ConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__SecretsServiceEndpoint", graphSecretsEndpoint),
                ("ApplicationTopology__SqlServer__Port", sqlPort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__ConfigurationServiceBasePort", configurationServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("ApplicationTopology__SecretsServiceBasePort", secretsServiceBasePort.ToString(CultureInfo.InvariantCulture)),
                ("CLOUDSHELL_LOCAL_HOSTS_FILE", hostsFilePath)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var graphVolume = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.volume:application-topology-sql-data");
        var graphSqlServer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-server:application-topology-sql-server");
        var graphDatabase = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.sql-database:application-topology-db");
        var graphSettings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:application-topology-settings");
        var graphSecrets = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:application-topology-secrets");
        var graphApi = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:application-topology-api");
        var graphFrontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:application-topology-frontend");
        var graphHostConfiguration = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.host:application-topology-host-settings");
        var graphDnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.dnsZone:application-topology-local");
        var nameMapping = Assert.Single(resources, resource =>
            resource.GetProperty("typeId").GetString() == NameMappingResourceTypeProvider.ResourceTypeId.ToString()
            && resource.GetProperty("attributes")
                .GetProperty(NameMappingResourceTypeProvider.Attributes.HostName.ToString())
                .GetString() == "app.application-topology.cloudshell.local");

        foreach (var oldResourceId in new[]
        {
            "cloudshell.storage:application-topology-local",
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
            "application.dotnet-app:application-topology-frontend",
            nameMapping.GetProperty("attributes")
                .GetProperty("nameMapping.targetResourceId")
                .GetString());
        var dnsReconcileAction = graphDnsZone
            .GetProperty("resourceActions")
            .GetProperty("reconcileNameMappings");
        var dnsReconcileHref = dnsReconcileAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The DNS zone reconcile action did not include an href.");
        var dnsReconcileJson = await host.SendAsync(HttpMethod.Post, dnsReconcileHref);
        using var dnsReconcileDocument = JsonDocument.Parse(dnsReconcileJson);
        Assert.Contains(
            "Published 1 local host name mapping(s)",
            dnsReconcileDocument.RootElement.GetProperty("message").GetString());
        var hostsFile = await File.ReadAllTextAsync(hostsFilePath);
        Assert.Contains("127.0.0.1 app.application-topology.cloudshell.local", hostsFile);
        Assert.Equal("cloudshell.volume", graphVolume.GetProperty("typeId").GetString());
        Assert.Equal("application.sql-server", graphSqlServer.GetProperty("typeId").GetString());
        Assert.Equal("application.sql-database", graphDatabase.GetProperty("typeId").GetString());
        Assert.Contains(
            "cloudshell.volume:application-topology-sql-data",
            graphSqlServer.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            "application.sql-server:application-topology-sql-server",
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
            $"/resources/{Uri.EscapeDataString("application.dotnet-app:application-topology-api")}/details");
        Assert.Contains("API", graphApiDetailsHtml);
        Assert.Contains(".NET App", graphApiDetailsHtml);
        Assert.Contains("application.dotnet-app", graphApiDetailsHtml);

        var graphApiEndpointsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.dotnet-app:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Endpoints.Value)}");
        Assert.Contains("Application exposure", graphApiEndpointsHtml);
        Assert.Contains("Add DNS name", graphApiEndpointsHtml);

        var graphApiConfigurationHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.dotnet-app:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Configuration.Value)}");
        Assert.Contains("Resource model", graphApiConfigurationHtml);
        Assert.Contains("project.path", graphApiConfigurationHtml);
        Assert.Contains("Capabilities and operations", graphApiConfigurationHtml);

        var graphApiEnvironmentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.dotnet-app:application-topology-api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Environment.Value)}");
        Assert.Contains("Environment variables", graphApiEnvironmentHtml);
        Assert.Contains("CLOUDSHELL_TRACE_INGEST_ENDPOINT", graphApiEnvironmentHtml);
        Assert.DoesNotContain("CLOUDSHELL_IDENTITY_CLIENT_SECRET", graphApiEnvironmentHtml);

        var graphSqlDetailsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:application-topology-sql-server")}/details");
        Assert.Contains("SQL Server", graphSqlDetailsHtml);
        Assert.Contains("application.sql-server", graphSqlDetailsHtml);

        var graphSqlConfigurationHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Configuration.Value)}");
        Assert.Contains("Resource model", graphSqlConfigurationHtml);
        Assert.Contains("version", graphSqlConfigurationHtml);
        Assert.Contains("Endpoints", graphSqlConfigurationHtml);
        Assert.Contains("Capabilities and operations", graphSqlConfigurationHtml);

        var graphSqlDatabasesHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:application-topology-sql-server")}/details?tab={Uri.EscapeDataString("application:databases")}");
        Assert.Contains("Databases", graphSqlDatabasesHtml);
        Assert.Contains("application_topology", graphSqlDatabasesHtml);
        Assert.Contains("application.sql-database:application-topology-db", graphSqlDatabasesHtml);

        var graphSqlStorageHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.sql-server:application-topology-sql-server")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Storage.Value)}");
        Assert.Contains("Storage", graphSqlStorageHtml);
        Assert.Contains("SQL Data", graphSqlStorageHtml);
        Assert.Contains("mount target unavailable", graphSqlStorageHtml);

        var graphApplicationAddHtml = await host.GetStringAsync(
            "/resources/add?type=application.dotnet-app");
        Assert.Contains(".NET App", graphApplicationAddHtml);
        Assert.Contains("Create an application resource from an uploaded .NET artifact.", graphApplicationAddHtml);
        Assert.DoesNotContain("Resource type not found", graphApplicationAddHtml);

        await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology settings");
        await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology secrets");
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphConfigurationEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);
        await host.WaitForAbsoluteHttpOkAsync(
            $"{graphSecretsEndpoint}/healthz",
            bearerToken: null,
            StartupTimeout);

        await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology API");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{apiPort}/health",
            bearerToken: null,
            StartupTimeout);
        var graphApiSettingsJson = await host.GetAbsoluteStringAsync(
            $"http://localhost:{apiPort}/settings");
        using var graphApiSettingsDocument = JsonDocument.Parse(graphApiSettingsJson);
        var graphApiSettings = graphApiSettingsDocument.RootElement;
        Assert.Equal("Hello from CloudShell resource configuration.", graphApiSettings.GetProperty("message").GetString());
        Assert.Equal("Resource model", graphApiSettings.GetProperty("mode").GetString());
        Assert.True(graphApiSettings.GetProperty("externalApiKeyConfigured").GetBoolean());

        await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology frontend");
        await host.WaitForAbsoluteHttpOkAsync(
            $"http://localhost:{frontendPort}/healthz",
            bearerToken: null,
            StartupTimeout);
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_RunsSqlBackedWorkload()
    {
        const string sqlContainerName = "cloudshell-application-topology-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.ContainerImage) ||
            await DockerComposeStack.ContainerExistsAsync(sqlContainerName))
        {
            return;
        }

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
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
                ("ApplicationTopology__ConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__SecretsServiceEndpoint", graphSecretsEndpoint),
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
            var graphVolume = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "cloudshell.volume:application-topology-sql-data");
            var graphSqlServer = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-server:application-topology-sql-server");
            var graphDatabase = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.sql-database:application-topology-db");
            var graphSettings = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "configuration.store:application-topology-settings");
            var graphSecrets = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "secrets.vault:application-topology-secrets");
            var graphApi = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.dotnet-app:application-topology-api");
            var graphFrontend = Assert.Single(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "application.dotnet-app:application-topology-frontend");
            Assert.DoesNotContain(
                resources,
                resource => resource.GetProperty("id").GetString() ==
                    "cloudshell.storage:application-topology-local");
            Assert.Equal("cloudshell.volume", graphVolume.GetProperty("typeId").GetString());

            await StartGraphResourceIfAvailableAsync(host, graphSqlServer, "ApplicationTopology SQL Server");
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:application-topology-sql-server",
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
                $"Expected resource-model storage-backed SQL data path '{sampleDataPath}' to be created.");

            var ensureCreatedHref = graphDatabase
                .GetProperty("resourceActions")
                .GetProperty(SqlDatabaseResourceTypeProvider.Operations.EnsureCreated.Value)
                .GetProperty("href")
                .GetString() ?? throw new InvalidOperationException("The SQL database ensure-created action did not include an href.");
            Assert.Contains(
                "/api/control-plane/v1/resources/application.sql-database%3Aapplication-topology-db/actions/application.sql-database.ensure-created",
                ensureCreatedHref);

            await StartGraphResourceIfAvailableAsync(host, graphSettings, "ApplicationTopology settings");
            await StartGraphResourceIfAvailableAsync(host, graphSecrets, "ApplicationTopology secrets");
            await StartGraphResourceIfAvailableAsync(host, graphApi, "ApplicationTopology API");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{apiPort}/health",
                bearerToken: null,
                StartupTimeout);
            var graphDatabaseJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{apiPort}/database",
                StartupTimeout);
            using var graphDatabaseDocument = JsonDocument.Parse(graphDatabaseJson);
            Assert.Equal("ok", graphDatabaseDocument.RootElement.GetProperty("status").GetString());
            Assert.Equal("mssql", graphDatabaseDocument.RootElement.GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphDatabaseDocument.RootElement.GetProperty("database").GetString());

            await StartGraphResourceIfAvailableAsync(host, graphFrontend, "ApplicationTopology frontend");
            await host.WaitForAbsoluteHttpOkAsync(
                $"http://localhost:{frontendPort}/healthz",
                bearerToken: null,
                StartupTimeout);
            var graphUpstreamJson = await host.WaitForAbsoluteHttpOkAndGetStringAsync(
                $"http://localhost:{frontendPort}/upstream",
                StartupTimeout);
            using var graphUpstreamDocument = JsonDocument.Parse(graphUpstreamJson);
            var graphUpstream = graphUpstreamDocument.RootElement;
            Assert.Equal("Application Topology Frontend", graphUpstream.GetProperty("frontend").GetString());
            Assert.Equal("https+http://application-topology-api", graphUpstream.GetProperty("logicalApiEndpoint").GetString());
            Assert.Equal("Hello from the referenced API project.", graphUpstream.GetProperty("upstream").GetProperty("message").GetString());
            Assert.Equal("Resource model", graphUpstream.GetProperty("settings").GetProperty("mode").GetString());
            Assert.True(graphUpstream.GetProperty("settings").GetProperty("externalApiKeyConfigured").GetBoolean());
            Assert.Equal("ok", graphUpstream.GetProperty("database").GetProperty("status").GetString());
            Assert.Equal("mssql", graphUpstream.GetProperty("database").GetProperty("provider").GetString());
            Assert.Equal("application_topology", graphUpstream.GetProperty("database").GetProperty("database").GetString());

            await StopResourceIfRunningAsync(host, "application.dotnet-app:application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.dotnet-app:application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:application-topology-sql-server");
            await WaitForResourceStateAsync(
                host,
                "application.sql-server:application-topology-sql-server",
                ResourceState.Stopped,
                StartupTimeout);
            Assert.True(
                await WaitForDockerContainerRemovedAsync(sqlContainerName, StartupTimeout),
                $"Expected Docker container '{sqlContainerName}' to be removed after SQL stop.");
            shouldCleanupSqlContainer = false;
        }
        finally
        {
            await StopResourceIfRunningAsync(host, "application.dotnet-app:application-topology-frontend");
            await StopResourceIfRunningAsync(host, "application.dotnet-app:application-topology-api");
            await StopResourceIfRunningAsync(host, "application.sql-server:application-topology-sql-server");
            if (shouldCleanupSqlContainer)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);
            }
        }
    }

    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ApplicationTopologyHost_SqlRuntimeStopsOnGracefulHostShutdown()
    {
        const string graphSqlServerResourceId = "application.sql-server:application-topology-sql-server";
        const string sqlContainerName = "cloudshell-application-topology-sql-server";
        if (!await DockerComposeStack.IsAvailableAsync() ||
            !await DockerComposeStack.IsImageAvailableAsync(SqlServerResources.ContainerImage))
        {
            return;
        }

        await DockerComposeStack.RemoveContainerIfExistsAsync(sqlContainerName);

        var apiPort = await GetFreePortAsync();
        var frontendPort = await GetFreePortAsync();
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
                ("ApplicationTopology__ConfigurationServiceEndpoint", graphConfigurationEndpoint),
                ("ApplicationTopology__SecretsServiceEndpoint", graphSecretsEndpoint),
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

            await StartGraphResourceIfAvailableAsync(host, graphSqlServer, "ApplicationTopology SQL Server");
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
    public async Task SettingsAndSecretsSample_RunsServicesAndApiWithoutOldProviderRecords()
    {
        var configurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var secretsEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var apiEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        const string settingsResourceId = "configuration.store:sample-app";
        const string secretsResourceId = "secrets.vault:sample-app";
        const string apiResourceId = "application.dotnet-app:settings-secrets-api";
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
            settings.GetProperty("attributes").GetProperty("settingCount").GetString());
        Assert.Equal(
            "1",
            secrets.GetProperty("attributes").GetProperty("secretCount").GetString());

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
            configurationDocument.RootElement.GetProperty("settings").EnumerateArray(),
            setting =>
                setting.GetProperty("name").GetString() == "Sample:Message" &&
                setting.GetProperty("value").GetString() == "Hello from a configuration setting");

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

    [Fact]
    public async Task DeviceRegistrySample_EnrollsCurrentDevice()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-device-registry-sample-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            await DeviceRegistrySample_EnrollsCurrentDeviceCore(directory);
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Test cleanup should not hide the original failure.
            }
        }
    }

    private static async Task DeviceRegistrySample_EnrollsCurrentDeviceCore(string directory)
    {
        var registryEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var registryMqttEndpoint = $"mqtt://127.0.0.1:{await GetFreePortAsync()}";
        var configurationEndpoint = $"http://127.0.0.1:{await GetFreePortAsync()}";
        var deviceAppPort = await GetFreePortAsync();
        var deviceAppEndpoint = $"http://127.0.0.1:{deviceAppPort}";
        const string registryResourceId = "iot.device-registry:devices";
        const string configurationResourceId = "configuration.store:device-settings";
        const string registryAdminClientId = "device-registry-admin";
        const string registryAdminClientSecret = "device-registry-admin-secret";
        const string registryEnrollmentToken = "local-development-device-enrollment-token";
        var signingKeyPem = CreateDevelopmentSigningKeyPem();
        var configurationDefinitionsPath = Path.Combine(directory, "configuration-stores.json");
        var registryDefinitionsPath = Path.Combine(directory, "device-registries.json");

        await File.WriteAllTextAsync(
            configurationDefinitionsPath,
            JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        id = configurationResourceId,
                        settings = new[]
                        {
                            new
                            {
                                name = "Device:Mode",
                                value = "factory-online"
                            }
                        }
                    }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        await File.WriteAllTextAsync(
            registryDefinitionsPath,
            JsonSerializer.Serialize(
                new[]
                {
                    new
                    {
                        id = registryResourceId,
                        heartbeatStaleAfterSeconds = 300,
                        enrollmentPolicy = new
                        {
                            subjectPrefixes = new[] { "device/" },
                            requiredClaims = new[]
                            {
                                new
                                {
                                    name = "manufacturer",
                                    value = "cloudshell"
                                }
                            }
                        },
                        enrollmentProfiles = new[]
                        {
                            new
                            {
                                name = "default",
                                policy = new
                                {
                                    subjectPrefixes = new[] { "device/" },
                                    requiredClaims = new[]
                                    {
                                        new
                                        {
                                            name = "manufacturer",
                                            value = "cloudshell"
                                        }
                                    }
                                },
                                permissionGrants = new[]
                                {
                                    new
                                    {
                                        targetResourceId = configurationResourceId,
                                        permission = ConfigurationStoreResourceOperationPermissions.ReadSettings
                                    }
                                }
                            }
                        }
                    }
                },
                new JsonSerializerOptions(JsonSerializerDefaults.Web)));

        using var configurationService = await SampleProcess.StartAsync(
            "CloudShell.ConfigurationStoreService/CloudShell.ConfigurationStoreService.csproj",
            new Uri(configurationEndpoint).Port,
            CreateServiceEnvironment(
                signingKeyPem,
                "CloudShell__ConfigurationStoreService__DefinitionsPath",
                configurationDefinitionsPath,
                "CloudShell__ConfigurationStoreService__ResourceId",
                configurationResourceId));
        using var registryService = await SampleProcess.StartAsync(
            "CloudShell.DeviceRegistryService/CloudShell.DeviceRegistryService.csproj",
            new Uri(registryEndpoint).Port,
            CreateServiceEnvironment(
                signingKeyPem,
                "CloudShell__DeviceRegistryService__DefinitionsPath",
                registryDefinitionsPath,
                "CloudShell__DeviceRegistryService__ResourceId",
                registryResourceId,
                [
                    ("CloudShell__DeviceRegistryService__MqttEndpoint", registryMqttEndpoint),
                    ("CloudShell__DeviceRegistryService__EnrollmentToken", registryEnrollmentToken),
                    ("Authentication__BuiltInAuthority__Clients__device-registry-admin__Secret", registryAdminClientSecret),
                    ("Authentication__BuiltInAuthority__Clients__device-registry-admin__Scopes__0", "ControlPlane.Access"),
                    ("Authentication__BuiltInAuthority__Clients__device-registry-admin__ResourcePermissions__0__ResourceId", registryResourceId),
                    ("Authentication__BuiltInAuthority__Clients__device-registry-admin__ResourcePermissions__0__Permission", DeviceRegistryResourceOperationPermissions.ManageDevices)
                ]));
        await configurationService.WaitForHttpOkAsync("/healthz", StartupTimeout);
        await registryService.WaitForHttpOkAsync("/healthz", StartupTimeout);

        using var deviceApp = await SampleProcess.StartAsync(
            "samples/DeviceRegistry/DeviceApp/CloudShell.DeviceRegistry.DeviceApp.csproj",
            deviceAppPort,
            [
                ("CLOUDSHELL_DEVICE_REGISTRY_ENDPOINT", registryEndpoint),
                ("CLOUDSHELL_DEVICE_REGISTRY_RESOURCE_ID", registryResourceId),
                ("CLOUDSHELL_DEVICE_REGISTRY_MQTT_ENDPOINT", registryMqttEndpoint),
                ("CLOUDSHELL_DEVICE_REGISTRY_ENROLLMENT_TOKEN", registryEnrollmentToken),
                ("CLOUDSHELL_CONFIGURATION_STORE_ENDPOINT", configurationEndpoint),
                ("CLOUDSHELL_CONFIGURATION_STORE_RESOURCE_ID", configurationResourceId),
                ("CLOUDSHELL_CONFIGURATION_SETTING_NAME", "Device:Mode"),
                ("DEVICE_MANUFACTURER", "cloudshell")
            ]);
        await deviceApp.WaitForHttpOkAsync("/health", StartupTimeout);

        using var enrollmentClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var enrollmentResponse = await enrollmentClient.PostAsync(
            $"{deviceAppEndpoint.TrimEnd('/')}/enroll-current-device",
            content: null);
        var enrollmentJson = await enrollmentResponse.Content.ReadAsStringAsync();
        Assert.True(
            enrollmentResponse.IsSuccessStatusCode,
            $"Device app enrollment returned {(int)enrollmentResponse.StatusCode} {enrollmentResponse.ReasonPhrase}: {enrollmentJson}");
        using var enrollmentDocument = JsonDocument.Parse(enrollmentJson);
        var enrollment = enrollmentDocument.RootElement
            .GetProperty("enrollment")
            .Deserialize<DeviceEnrollmentResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
            throw new JsonException("Device Registry sample returned no enrollment response.");
        var heartbeat = enrollmentDocument.RootElement
            .GetProperty("heartbeat")
            .Deserialize<DeviceMetadataResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
            throw new JsonException("Device Registry sample returned no heartbeat response.");
        var sync = enrollmentDocument.RootElement
            .GetProperty("sync")
            .Deserialize<DeviceSyncResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
            throw new JsonException("Device Registry sample returned no sync response.");
        var mqttSync = enrollmentDocument.RootElement
            .GetProperty("mqttSync")
            .Deserialize<DeviceSyncResponse>(
                new JsonSerializerOptions(JsonSerializerDefaults.Web)) ??
            throw new JsonException("Device Registry sample returned no MQTT sync response.");
        var mqttSyncPublished = enrollmentDocument.RootElement
            .GetProperty("mqttSyncPublished")
            .GetBoolean();
        var configurationSetting = enrollmentDocument.RootElement.GetProperty("configuration");

        Assert.Equal("iot.device-registry:devices", enrollment.RegistryId);
        Assert.StartsWith("device/", enrollment.Subject);
        Assert.Equal("deviceIdentity", enrollment.IdentityCategory);
        Assert.Equal(ResourcePrincipalKind.DeviceIdentity, enrollment.Principal.Kind);
        Assert.Equal("built-in", enrollment.Principal.ProviderId);
        Assert.Equal("iot.device-registry:devices", enrollment.Principal.SourceResourceId);
        Assert.Equal(enrollment.DeviceId, enrollment.Principal.SourceIdentityName);
        Assert.Equal("built-in", enrollment.IdentityProviderId);
        Assert.Equal("iot.device-registry:devices", enrollment.IdentityResourceId);
        Assert.Equal(enrollment.DeviceId, enrollment.IdentityName);
        Assert.Equal(
            $"iot.device-registry:devices/{enrollment.DeviceId}",
            enrollment.ClientId);
        Assert.False(string.IsNullOrWhiteSpace(enrollment.ClientSecret));
        Assert.Equal("cloudshell", enrollment.Claims["manufacturer"]);
        Assert.False(string.IsNullOrWhiteSpace(enrollment.Properties["platform"]));
        Assert.False(string.IsNullOrWhiteSpace(enrollment.Properties["osDescription"]));
        Assert.False(string.IsNullOrWhiteSpace(enrollment.Properties["frameworkDescription"]));
        Assert.Equal("active", enrollment.Status);
        Assert.Equal("online", enrollment.Presence);
        Assert.Equal("default", enrollment.EnrollmentProfileName);
        Assert.Equal("group", enrollment.EnrollmentProfileKind);
        Assert.NotNull(enrollment.LastSeenAt);
        Assert.Equal("enrollment", enrollment.LastSeenSource);
        Assert.Equal("http", enrollment.LastSeenTransport);
        Assert.Equal(enrollment.DeviceId, heartbeat.DeviceId);
        Assert.Equal("active", heartbeat.Status);
        Assert.Equal("online", heartbeat.Presence);
        Assert.Equal("default", heartbeat.EnrollmentProfileName);
        Assert.Equal("group", heartbeat.EnrollmentProfileKind);
        Assert.NotNull(heartbeat.LastSeenAt);
        Assert.Equal("sample-app", heartbeat.LastSeenSource);
        Assert.Equal("http", heartbeat.LastSeenTransport);
        Assert.Equal("device-app", heartbeat.Properties["sample.app"]);
        Assert.Equal(enrollment.DeviceId, sync.Device.DeviceId);
        Assert.Equal("default", sync.Device.EnrollmentProfileName);
        Assert.Equal("group", sync.Device.EnrollmentProfileKind);
        Assert.Equal("sample-app", sync.Device.LastSeenSource);
        Assert.Equal("http", sync.Device.LastSeenTransport);
        Assert.Equal("device-app", sync.Device.Properties["sample.sync"]);
        Assert.False(sync.DesiredStateChanged);
        Assert.Equal(0, sync.Desired.Version);
        Assert.Equal(1, sync.Reported.Version);
        Assert.Equal("running", sync.Reported.State["mode"].GetString());
        Assert.Equal("Device:Mode", sync.Reported.State["configurationSetting"].GetString());
        Assert.True(mqttSyncPublished);
        Assert.Equal(enrollment.DeviceId, mqttSync.Device.DeviceId);
        Assert.Equal("sample-app-mqtt", mqttSync.Device.LastSeenSource);
        Assert.Equal("mqtt", mqttSync.Device.LastSeenTransport);
        Assert.False(mqttSync.DesiredStateChanged);
        Assert.Equal(0, mqttSync.Desired.Version);
        Assert.Equal(2, mqttSync.Reported.Version);
        Assert.Equal("mqtt", mqttSync.Reported.State["transport"].GetString());
        Assert.Equal("Device:Mode", configurationSetting.GetProperty("name").GetString());
        Assert.Equal("factory-online", configurationSetting.GetProperty("value").GetString());

        using var tokenClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var tokenResponse = await tokenClient.PostAsync(
            enrollment.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = enrollment.ClientId,
                ["client_secret"] = enrollment.ClientSecret,
                ["scope"] = "ControlPlane.Access"
            }));
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(
            tokenResponse.IsSuccessStatusCode,
            $"Device identity token request returned {(int)tokenResponse.StatusCode} {tokenResponse.ReasonPhrase}: {tokenJson}");
        using var tokenDocument = JsonDocument.Parse(tokenJson);
        Assert.False(string.IsNullOrWhiteSpace(
            tokenDocument.RootElement.GetProperty("access_token").GetString()));

        var adminToken = await RequestClientCredentialsTokenAsync(
            enrollment.TokenEndpoint,
            registryAdminClientId,
            registryAdminClientSecret);
        var registryClient = new DeviceRegistryClient(new Uri(registryEndpoint), tokenClient);
        var devicesAfterMqttSync = await registryClient.GetDevicesAsync(
            registryResourceId,
            adminToken);
        var deviceAfterMqttSync = Assert.Single(devicesAfterMqttSync);
        Assert.Equal(enrollment.DeviceId, deviceAfterMqttSync.DeviceId);
        Assert.Equal("sample-app-mqtt", deviceAfterMqttSync.LastSeenSource);
        Assert.Equal("mqtt", deviceAfterMqttSync.LastSeenTransport);
        Assert.Equal("device-app", deviceAfterMqttSync.Properties["sample.mqttSync"]);

        Assert.Equal(
            MqttClientConnectResultCode.BadUserNameOrPassword,
            await ConnectDeviceRegistryMqttAsync(
                registryMqttEndpoint,
                enrollment.DeviceId,
                enrollment.ClientId,
                "invalid-secret"));

        _ = await PublishDeviceRegistryMqttAsync(
            registryMqttEndpoint,
            enrollment.DeviceId,
            enrollment.ClientId,
            enrollment.ClientSecret,
            DeviceRegistryMqttTopicNames.BuildSyncTopic(
                registryResourceId,
                enrollment.DeviceId),
            "{");

        _ = await PublishDeviceRegistryMqttAsync(
            registryMqttEndpoint,
            enrollment.DeviceId,
            enrollment.ClientId,
            enrollment.ClientSecret,
            $"cloudshell/device-registries/{Uri.EscapeDataString(registryResourceId)}/devices/{enrollment.DeviceId}/unsupported",
            "{}");

        var devicesAfterRejectedMqttPublishes = await registryClient.GetDevicesAsync(
            registryResourceId,
            adminToken);
        var deviceAfterRejectedMqttPublishes = Assert.Single(devicesAfterRejectedMqttPublishes);
        Assert.Equal("sample-app-mqtt", deviceAfterRejectedMqttPublishes.LastSeenSource);
        Assert.Equal("mqtt", deviceAfterRejectedMqttPublishes.LastSeenTransport);
        Assert.Equal("device-app", deviceAfterRejectedMqttPublishes.Properties["sample.mqttSync"]);
        var twinAfterRejectedMqttPublishes = await registryClient.GetDeviceTwinAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken);
        Assert.Equal(2, twinAfterRejectedMqttPublishes.Reported.Version);
        Assert.Equal("mqtt", twinAfterRejectedMqttPublishes.Reported.State["transport"].GetString());

        var desired = await registryClient.SetDesiredStateAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken,
            new DeviceDesiredStateRequest(
                new Dictionary<string, JsonElement>
                {
                    ["mode"] = JsonSerializer.SerializeToElement("eco")
                }));
        Assert.Equal(1, desired.Desired.Version);
        Assert.Equal("eco", desired.Desired.State["mode"].GetString());

        var followUpMqttSync = await new DeviceRegistryMqttClient(new Uri(registryMqttEndpoint))
            .SyncDeviceAsync(
                registryResourceId,
                enrollment.DeviceId,
                enrollment.ClientId,
                enrollment.ClientSecret,
                new DeviceSyncRequest(
                    new Dictionary<string, JsonElement>
                    {
                        ["mode"] = JsonSerializer.SerializeToElement("running")
                    },
                    Source: "sample-mqtt-follow-up",
                    LastKnownDesiredVersion: 0));
        Assert.True(followUpMqttSync.DesiredStateChanged);
        Assert.Equal(1, followUpMqttSync.Desired.Version);
        Assert.Equal("eco", followUpMqttSync.Desired.State["mode"].GetString());
        Assert.Equal("mqtt", followUpMqttSync.Device.LastSeenTransport);
        Assert.Equal(3, followUpMqttSync.Reported.Version);
        Assert.Equal("running", followUpMqttSync.Reported.State["mode"].GetString());

        var disabled = await registryClient.DisableDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken,
            "sample maintenance");
        Assert.Equal("disabled", disabled.Status);
        Assert.Equal("disabled", disabled.Presence);
        Assert.NotNull(disabled.DisabledAt);
        Assert.Equal("sample maintenance", disabled.DisabledReason);
        Assert.Equal(
            MqttClientConnectResultCode.BadUserNameOrPassword,
            await ConnectDeviceRegistryMqttAsync(
                registryMqttEndpoint,
                enrollment.DeviceId,
                enrollment.ClientId,
                enrollment.ClientSecret));

        using var disabledTokenResponse = await tokenClient.PostAsync(
            enrollment.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = enrollment.ClientId,
                ["client_secret"] = enrollment.ClientSecret,
                ["scope"] = "ControlPlane.Access"
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, disabledTokenResponse.StatusCode);

        var enabled = await registryClient.EnableDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken);
        Assert.Equal("active", enabled.Status);
        Assert.Equal("online", enabled.Presence);
        Assert.Null(enabled.DisabledAt);
        Assert.Null(enabled.DisabledReason);
        Assert.Equal(
            MqttClientConnectResultCode.Success,
            await ConnectDeviceRegistryMqttAsync(
                registryMqttEndpoint,
                enrollment.DeviceId,
                enrollment.ClientId,
                enrollment.ClientSecret));
        var enabledDeviceToken = await RequestClientCredentialsTokenAsync(
            enrollment.TokenEndpoint,
            enrollment.ClientId,
            enrollment.ClientSecret);
        Assert.False(string.IsNullOrWhiteSpace(enabledDeviceToken));

        var revoked = await registryClient.RevokeDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken,
            "sample cleanup");
        Assert.Equal("revoked", revoked.Status);
        Assert.Equal("revoked", revoked.Presence);
        Assert.NotNull(revoked.RevokedAt);
        Assert.Equal("sample cleanup", revoked.RevokedReason);
        Assert.Equal(
            MqttClientConnectResultCode.BadUserNameOrPassword,
            await ConnectDeviceRegistryMqttAsync(
                registryMqttEndpoint,
                enrollment.DeviceId,
                enrollment.ClientId,
                enrollment.ClientSecret));

        using var revokedTokenResponse = await tokenClient.PostAsync(
            enrollment.TokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = enrollment.ClientId,
                ["client_secret"] = enrollment.ClientSecret,
                ["scope"] = "ControlPlane.Access"
            }));
        Assert.Equal(HttpStatusCode.Unauthorized, revokedTokenResponse.StatusCode);

        await registryClient.RemoveDeviceAsync(
            registryResourceId,
            enrollment.DeviceId,
            adminToken);
        var remainingDevices = await registryClient.GetDevicesAsync(
            registryResourceId,
            adminToken);
        Assert.DoesNotContain(
            remainingDevices,
            device => string.Equals(
                device.DeviceId,
                enrollment.DeviceId,
                StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<MqttClientConnectResultCode> ConnectDeviceRegistryMqttAsync(
        string endpoint,
        string deviceId,
        string clientId,
        string clientSecret)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        var result = await client.ConnectAsync(
            CreateMqttClientOptions(endpoint, deviceId, clientId, clientSecret)
                .WithoutThrowOnNonSuccessfulConnectResponse()
                .Build());
        if (client.IsConnected)
        {
            await client.DisconnectAsync(new());
        }

        return result.ResultCode;
    }

    private static async Task<MqttClientPublishResult> PublishDeviceRegistryMqttAsync(
        string endpoint,
        string deviceId,
        string clientId,
        string clientSecret,
        string topic,
        string payload)
    {
        var factory = new MqttFactory();
        using var client = factory.CreateMqttClient();
        await client.ConnectAsync(
            CreateMqttClientOptions(endpoint, deviceId, clientId, clientSecret)
                .Build());
        var result = await client.PublishAsync(
            new MqttApplicationMessageBuilder()
                .WithTopic(topic)
                .WithPayload(payload)
                .WithQualityOfServiceLevel(MqttQualityOfServiceLevel.AtLeastOnce)
                .Build());
        await client.DisconnectAsync(new());
        return result;
    }

    private static MqttClientOptionsBuilder CreateMqttClientOptions(
        string endpoint,
        string deviceId,
        string clientId,
        string clientSecret)
    {
        var uri = new Uri(endpoint);
        return new MqttClientOptionsBuilder()
            .WithTcpServer(uri.Host, uri.Port > 0 ? uri.Port : 1883)
            .WithClientId($"cloudshell-sample-{deviceId}-{Guid.NewGuid():N}")
            .WithCredentials(clientId, clientSecret)
            .WithProtocolVersion(MqttProtocolVersion.V500)
            .WithCleanSession();
    }

    private static async Task<string> RequestClientCredentialsTokenAsync(
        string tokenEndpoint,
        string clientId,
        string clientSecret)
    {
        using var tokenClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        using var tokenResponse = await tokenClient.PostAsync(
            tokenEndpoint,
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["scope"] = "ControlPlane.Access"
            }));
        var tokenJson = await tokenResponse.Content.ReadAsStringAsync();
        Assert.True(
            tokenResponse.IsSuccessStatusCode,
            $"Client credentials token request returned {(int)tokenResponse.StatusCode} {tokenResponse.ReasonPhrase}: {tokenJson}");
        using var tokenDocument = JsonDocument.Parse(tokenJson);
        return tokenDocument.RootElement.GetProperty("access_token").GetString() ??
            throw new JsonException("Token response did not include an access token.");
    }

    private static IReadOnlyList<(string Key, string Value)> CreateServiceEnvironment(
        string signingKeyPem,
        string definitionsVariableName,
        string definitionsPath,
        string resourceIdVariableName,
        string resourceId,
        IReadOnlyList<(string Key, string Value)>? additionalVariables = null)
    {
        var variables = new List<(string Key, string Value)>
        {
            ("Authentication__BuiltInAuthority__Enabled", "true"),
            ("Authentication__BuiltInAuthority__Issuer", "http://localhost"),
            ("Authentication__BuiltInAuthority__Audience", "cloudshell-control-plane"),
            ("Authentication__BuiltInAuthority__SigningKeyPem", signingKeyPem),
            (definitionsVariableName, definitionsPath),
            (resourceIdVariableName, resourceId)
        };
        if (additionalVariables is not null)
        {
            variables.AddRange(additionalVariables);
        }

        return variables;
    }

    private static string CreateDevelopmentSigningKeyPem()
    {
        using var rsa = RSA.Create(2048);
        return rsa.ExportRSAPrivateKeyPem();
    }

    private static string CreateCurrentDeviceSubject()
    {
        var machineName = Environment.MachineName;
        if (string.IsNullOrWhiteSpace(machineName))
        {
            machineName = "current";
        }

        var characters = machineName
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '-')
            .ToArray();
        var normalized = new string(characters).Trim('-');

        return $"device/{(string.IsNullOrWhiteSpace(normalized) ? "current" : normalized)}";
    }

    private static string CreateDeviceId(
        string registryId,
        string subject)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{registryId}\u001f{subject}"));
        return $"device-{Convert.ToHexString(bytes)[..24].ToLowerInvariant()}";
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
        resource = await RefreshGraphResourceAsync(host, resource);
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

    private static async Task<JsonElement> RefreshGraphResourceAsync(
        SampleProcess host,
        JsonElement resource)
    {
        var resourceId = resource.GetProperty("id").GetString();
        if (string.IsNullOrWhiteSpace(resourceId))
        {
            return resource;
        }

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        foreach (var candidate in resourcesDocument.RootElement.EnumerateArray())
        {
            if (string.Equals(
                candidate.GetProperty("id").GetString(),
                resourceId,
                StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Clone();
            }
        }

        return resource;
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
            resource.GetProperty("id").GetString() == "cloudshell.identity-provisioning:keycloak");
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:third-party-identity");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:keycloak-provisioned-api");
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
        Assert.Equal("http://localhost:5138", settingsAttributes.GetProperty("endpoint").GetString());
        Assert.Equal("1", settingsAttributes.GetProperty("settingCount").GetString());
        Assert.Equal("application.dotnet-app", api.GetProperty("typeId").GetString());
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

        var identityAddHtml = await host.GetStringAsync(
            "/resources/add?type=cloudshell.identity-provisioning");
        Assert.Contains("Resource model resources", identityAddHtml);
        Assert.DoesNotContain("Resource type not found", identityAddHtml);

        var configurationStoreAddHtml = await host.GetStringAsync(
            "/resources/add?type=configuration.store");
        Assert.Contains("Resource model resources", configurationStoreAddHtml);
        Assert.DoesNotContain("Resource type not found", configurationStoreAddHtml);
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
            resource.GetProperty("id").GetString() == "cloudshell.identity-provisioning:keycloak");
        var settings = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "configuration.store:third-party-identity");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:keycloak-provisioned-api");

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
            configurationRoot.GetProperty("settings").EnumerateArray(),
            setting =>
                setting.GetProperty("name").GetString() == "Sample:Message" &&
                setting.GetProperty("value").GetString() == "Hello from a Keycloak-provisioned resource identity");

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
            resource.GetProperty("id").GetString() == "cloudshell.network:split-sample");
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
        var resources = document.RootElement.EnumerateArray().ToArray();
        var resourceIds = resources
            .Select(resource => resource.GetProperty("id").GetString())
            .OfType<string>()
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(["sample:api", "sample:database", "sample:worker"], resourceIds);

        var apiResource = resources.Single(resource =>
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
            .OrderBy(id => id, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        Assert.Equal(["sample:api", "sample:database"], resourceIds);

        using var apiResponse = await client.GetAsync(
            "/api/control-plane/v1/resources/sample%3Aapi");
        Assert.Equal(HttpStatusCode.OK, apiResponse.StatusCode);

        using var workerResponse = await client.GetAsync(
            "/api/control-plane/v1/resources/sample%3Aworker");
        Assert.Equal(HttpStatusCode.NotFound, workerResponse.StatusCode);

        var databaseJson = await client.GetStringAsync(
            "/api/control-plane/v1/resources/sample%3Adatabase");
        using var databaseDocument = JsonDocument.Parse(databaseJson);
        Assert.Equal("sample:database", databaseDocument.RootElement.GetProperty("id").GetString());
        var stopHref = databaseDocument.RootElement
            .GetProperty("resourceActions")
            .GetProperty("stop")
            .GetProperty("href")
            .GetString() ?? throw new InvalidOperationException("The database stop action did not include an href.");
        using var stopResponse = await client.PostAsync($"{stopHref}?ignoreDependentWarning=true", null);
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
            resource.GetProperty("id").GetString() == "docker.host:sample");
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

        var startAction = app
            .GetProperty("resourceActions")
            .GetProperty(ContainerApplicationResourceTypeProvider.Operations.Start.Value);
        var startHref = startAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The container app start action did not include an href.");
        var startJson = await host.SendAsync(HttpMethod.Post, startHref);
        using var startDocument = JsonDocument.Parse(startJson);
        Assert.Contains(
            "deferred container app runtime bridge",
            startDocument.RootElement.GetProperty("message").GetString());

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
        const string registryContainerName = "cloudshell-container-app-deployment-registry";
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
        const string registryContainerName = "cloudshell-container-app-deployment-registry";
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
    [Trait("Category", "DockerIntegration")]
    public async Task SignalRContainerAppSample_DeclaresFrontendAndContainerAppIntent()
    {
        var ports = await GetFreePortRangeAsync(6);
        var hostPort = ports[0];
        var apiPort = ports[1];
        var replicaPortStart = ports[2];
        var frontendPort = ports[5];
        using var host = await SampleProcess.StartAsync(
            "samples/SignalRContainerApp/CloudShell.SignalRContainerApp.csproj",
            hostPort,
            [
                ("SignalRContainerApp__ApiPort", apiPort.ToString(CultureInfo.InvariantCulture)),
                ("SignalRContainerApp__ReplicaPortStart", replicaPortStart.ToString(CultureInfo.InvariantCulture)),
                ("SignalRContainerApp__FrontendEndpoint", $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}"),
                ("ResourceManager__AllowLocalPathResourceDefinitions", "true")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var frontend = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.dotnet-app:signalr-frontend");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:signalr-api");
        var containerHost = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.container-host:default");
        var apiAttributes = api.GetProperty("attributes");
        var frontendAttributes = frontend.GetProperty("attributes");
        var containerHostAttributes = containerHost.GetProperty("attributes");

        Assert.Equal("application.dotnet-app", frontend.GetProperty("typeId").GetString());
        Assert.Equal("application.container-app", api.GetProperty("typeId").GetString());
        Assert.Equal("cloudshell.container-host", containerHost.GetProperty("typeId").GetString());
        Assert.Equal("SignalR Frontend", frontend.GetProperty("displayName").GetString());
        Assert.Equal("SignalR API", api.GetProperty("displayName").GetString());
        Assert.Equal("Default container host", containerHost.GetProperty("displayName").GetString());
        Assert.Equal("Docker", containerHostAttributes.GetProperty("container.host.kind").GetString());
        Assert.Equal("true", containerHostAttributes.GetProperty("container.host.default").GetString());
        Assert.Equal(
            $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}",
            GetPrimaryEndpointAddress(frontend));
        Assert.Equal(
            $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}",
            GetPrimaryEndpointAddress(api));
        Assert.Equal(
            "cloudshell-signalr-api:20260630.1",
            apiAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", apiAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("Cookie", apiAttributes.GetProperty("container.routing.sessionAffinity.mode").GetString());
        Assert.Equal("CloudShellSignalRReplica", apiAttributes.GetProperty("container.routing.sessionAffinity.cookieName").GetString());
        Assert.Equal("300", apiAttributes.GetProperty("container.routing.sessionAffinity.durationSeconds").GetString());
        var apiObservability = api.GetProperty("observability");
        Assert.True(apiObservability.GetProperty("logs").GetBoolean());
        Assert.True(apiObservability.GetProperty("traces").GetBoolean());
        Assert.True(apiObservability.GetProperty("metrics").GetBoolean());
        Assert.Contains(
            $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}",
            frontendAttributes.GetRawText());
        Assert.Contains(
            "cloudshell.container-host:default",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));

        await AssertExportedSignalRTemplateCanRoundTripAndApplyAsync(host);

        try
        {
            await StartGraphResourceIfAvailableAsync(host, api, "SignalR API");
            await WaitForHttpSuccessAsync(
                $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}/health",
                StartupTimeout);
            await AssertSignalRRuntimeReplicaResourcesAsync(host);
            await AssertSignalRRuntimeReplicaMonitoringSnapshotsAsync(host);
            await AssertSignalRReplicaResourceLinksAsync(host);
            await AssertSignalRReplicaMonitoringMetricsFallbackAsync(host);
            await AssertSignalRReplicaLogSourcesAsync(host);
            await StartGraphResourceIfAvailableAsync(host, frontend, "SignalR Frontend");
            await WaitForHttpSuccessAsync(
                $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/",
                StartupTimeout);
            using var frontendClient = new HttpClient();
            var frontendIndex = await frontendClient.GetStringAsync(
                $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/");
            var scriptPrefix = "src=\"_framework/";
            var scriptStart = frontendIndex.IndexOf(scriptPrefix, StringComparison.Ordinal);
            Assert.True(scriptStart >= 0, frontendIndex);
            scriptStart += scriptPrefix.Length;
            var scriptEnd = frontendIndex.IndexOf('"', scriptStart);
            Assert.True(scriptEnd > scriptStart, frontendIndex);
            var frameworkScript = frontendIndex[scriptStart..scriptEnd];
            Assert.StartsWith("blazor.webassembly", frameworkScript);
            Assert.EndsWith(".js", frameworkScript);
            await WaitForHttpSuccessAsync(
                $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/_framework/{frameworkScript}",
                StartupTimeout);
            var frontendConfig = await frontendClient.GetStringAsync(
                $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/sample-config.json");
            Assert.Contains("/signalr-backend", frontendConfig);
            await WaitForHttpSuccessAsync(
                $"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/signalr-backend/health",
                StartupTimeout);

            var connected = new TaskCompletionSource<SignalRReplicaTestMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var echoed = new TaskCompletionSource<SignalRReplicaTestMessage>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            await using var connection = new HubConnectionBuilder()
                .WithUrl($"http://localhost:{frontendPort.ToString(CultureInfo.InvariantCulture)}/signalr-backend/hubs/replicas")
                .Build();
            connection.On<SignalRReplicaTestMessage>(
                "ReplicaConnected",
                message => connected.TrySetResult(message));
            connection.On<SignalRReplicaTestMessage>(
                "ReplicaMessage",
                message => echoed.TrySetResult(message));

            await connection.StartAsync();
            var connectedMessage = await connected.Task.WaitAsync(StartupTimeout);
            await connection.InvokeAsync("SendMessage", "Smoke test message");
            var echoedMessage = await echoed.Task.WaitAsync(StartupTimeout);

            Assert.Equal("Connected to SignalR backend.", connectedMessage.Text);
            Assert.Equal("Smoke test message", echoedMessage.Text);
            Assert.Equal(connectedMessage.Replica, echoedMessage.Replica);
            Assert.Equal(connectedMessage.ConnectionId, echoedMessage.ConnectionId);

            const string telemetryResourceId = "application.container-app:signalr-api";
            Assert.NotEmpty(await WaitForTraceSpansByResourceAsync(
                host,
                telemetryResourceId,
                StartupTimeout,
                spans => spans.Any(span => IsSignalRSpan(
                    span,
                    telemetryResourceId,
                    connectedMessage.Replica,
                    "SignalR message broadcast"))));
            Assert.NotEmpty(await WaitForMetricPointsAsync(
                host,
                telemetryResourceId,
                StartupTimeout,
                points => points.Any(point => IsSignalRMetric(
                    point,
                    telemetryResourceId,
                    connectedMessage.Replica,
                    "signalr.server.messages"))));
        }
        finally
        {
            await StopGraphResourceIfAvailableAsync(host, frontend, "SignalR Frontend");
            await StopGraphResourceIfAvailableAsync(host, api, "SignalR API");
        }
    }

    private static async Task AssertExportedSignalRTemplateCanRoundTripAndApplyAsync(
        SampleProcess host)
    {
        using var client = new HttpClient
        {
            BaseAddress = host.BaseAddress,
            Timeout = StartupTimeout
        };

        var exportResponse = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resource-templates/export",
            new ResourceTemplateExportRequest(
                "SignalR Container App",
                [
                    "application.container-app:signalr-api",
                    "application.dotnet-app:signalr-frontend"
                ],
                Metadata: new Dictionary<string, string>
                {
                    ["resourceGroup.id"] = "signalr-container-app"
                }));
        var exported = await ReadJsonAsync<ResourceTemplateExportResult>(exportResponse);

        Assert.False(exported.HasErrors, FormatDiagnostics(exported.Diagnostics));

        var yaml = CloudShell.ResourceModel.ResourceTemplateSerializer.SerializeTemplate(
            exported.Template);

        Assert.Contains("dependsOn:", yaml);
        Assert.Contains("resourceId: cloudshell.container-host:default", yaml);
        Assert.Contains("container:", yaml);
        Assert.Contains("image: cloudshell-signalr-api:20260630.1", yaml);
        Assert.Contains("logs:", yaml);
        Assert.Contains("sources:", yaml);
        Assert.Contains("health:", yaml);
        Assert.Contains("checks:", yaml);
        Assert.Contains("network:", yaml);
        Assert.Contains("resourceId: network:host", yaml);
        Assert.DoesNotContain("attributes:", yaml);
        Assert.DoesNotContain("container.image:", yaml);
        Assert.DoesNotContain("logs.sources:", yaml);
        Assert.DoesNotContain("health.checks:", yaml);
        Assert.DoesNotContain("value: cloudshell.container-host:default", yaml);
        Assert.DoesNotContain("addressingMode: resourceId", yaml);

        var roundTripped = CloudShell.ResourceModel.ResourceTemplateSerializer.DeserializeTemplate(yaml);
        var applyResponse = await client.PostAsJsonAsync(
            "/api/control-plane/v1/resource-templates/apply",
            new ResourceTemplateApplyRequest(roundTripped));
        var applied = await ReadJsonAsync<ResourceTemplateApplyResult>(applyResponse);

        Assert.False(applied.HasErrors, FormatDiagnostics(applied.Diagnostics));
        Assert.True(applied.IsCommitted);
    }

    private static async Task<TValue> ReadJsonAsync<TValue>(HttpResponseMessage response)
    {
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.IsSuccessStatusCode,
                $"Expected HTTP success but got {(int)response.StatusCode} ({response.ReasonPhrase}). {content}");
            var value = JsonSerializer.Deserialize<TValue>(
                content,
                CloudShell.ResourceModel.ResourceDefinitionJson.Options);
            Assert.NotNull(value);
            return value;
        }
    }

    private static string FormatDiagnostics(
        IReadOnlyList<CloudShell.ResourceModel.ResourceDefinitionDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(diagnostic =>
            $"{diagnostic.Severity}: {diagnostic.Code}: {diagnostic.Message} ({diagnostic.Target})"));

    private sealed record SignalRReplicaTestMessage(
        string Text,
        string Replica,
        string Resource,
        string Machine,
        string ConnectionId,
        DateTimeOffset Timestamp);

    private static bool IsSignalRSpan(
        JsonElement span,
        string resourceId,
        string replica,
        string name) =>
        span.GetProperty("name").GetString() == name &&
        span.GetProperty("resourceId").GetString() == resourceId &&
        span.TryGetProperty("spanAttributes", out var attributes) &&
        attributes.TryGetProperty("signalr.hub", out var hub) &&
        hub.GetString() == "ReplicaHub" &&
        attributes.TryGetProperty("runtime.replica.ordinal", out var replicaOrdinal) &&
        replicaOrdinal.GetString() == replica;

    private static bool IsSignalRMetric(
        JsonElement point,
        string resourceId,
        string replica,
        string name) =>
        point.GetProperty("name").GetString() == name &&
        point.GetProperty("resourceId").GetString() == resourceId &&
        point.TryGetProperty("attributes", out var attributes) &&
        attributes.TryGetProperty("signalr.hub", out var hub) &&
        hub.GetString() == "ReplicaHub" &&
        attributes.TryGetProperty("runtime.replica.ordinal", out var replicaOrdinal) &&
        replicaOrdinal.GetString() == replica;

    private static async Task AssertSignalRRuntimeReplicaResourcesAsync(SampleProcess host)
    {
        const string apiResourceId = "application.container-app:signalr-api";
        var expectedServiceId = ResourceOrchestratorReplicaGroups.CreateDefaultServiceName(apiResourceId);
        var expectedRevisionId = ContainerApplicationRuntimeRevisions.CreateImageRevisionId(
            ContainerRegistryDefaults.Default,
            "cloudshell-signalr-api:20260630.1");
        var expectedReplicaGroupId = ResourceOrchestratorReplicaGroups.CreateReplicaGroupId(
            expectedServiceId,
            expectedRevisionId);
        var expectedDefinition = LocalDockerContainerApplicationRuntimeDefinition.CreateDefault(apiResourceId);
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        string? lastBody = null;
        do
        {
            lastBody = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(lastBody);
            var replicas = resourcesDocument.RootElement
                .EnumerateArray()
                .Where(resource =>
                    string.Equals(resource.GetProperty("typeId").GetString(), "runtime.container", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(resource.GetProperty("ownerResourceId").GetString(), apiResourceId, StringComparison.OrdinalIgnoreCase) &&
                    resource.TryGetProperty("attributes", out var attributes) &&
                    attributes.TryGetProperty(ResourceAttributeNames.RuntimeKind, out var runtimeKind) &&
                    string.Equals(runtimeKind.GetString(), "containerReplica", StringComparison.OrdinalIgnoreCase))
                .OrderBy(resource => resource.GetProperty("id").GetString(), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (replicas.Length == 3)
            {
                for (var replica = 1; replica <= 3; replica++)
                {
                    var replicaText = replica.ToString(CultureInfo.InvariantCulture);
                    var resource = replicas[replica - 1];
                    var attributes = resource.GetProperty("attributes");
                    Assert.Equal(
                        LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(
                            expectedDefinition,
                            replica),
                        resource.GetProperty("id").GetString());
                    Assert.Equal(apiResourceId, resource.GetProperty("parentResourceId").GetString());
                    Assert.Equal((int)ResourceState.Running, resource.GetProperty("state").GetInt32());
                    Assert.Equal(expectedServiceId, attributes.GetProperty(ResourceAttributeNames.DeploymentServiceId).GetString());
                    Assert.Equal(expectedReplicaGroupId, attributes.GetProperty(ResourceAttributeNames.DeploymentReplicaGroupId).GetString());
                    Assert.Equal("localDockerContainerApplication", attributes.GetProperty(ResourceAttributeNames.RuntimeMaterialization).GetString());
                    Assert.Equal(replicaText, attributes.GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString());
                    Assert.Equal("3", attributes.GetProperty(ResourceAttributeNames.RuntimeReplicaCount).GetString());
                    Assert.Equal(expectedRevisionId, attributes.GetProperty(ResourceAttributeNames.RuntimeRevision).GetString());

                    var observabilityAttributes = resource
                        .GetProperty("observability")
                        .GetProperty("attributes");
                    Assert.Equal(expectedRevisionId, observabilityAttributes.GetProperty(TelemetryAttributeNames.DeploymentRevision).GetString());
                }

                return;
            }

            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        throw new TimeoutException(
            $"Timed out waiting for SignalR Docker runtime replica resources. Last response: {lastBody}");
    }

    private static async Task AssertSignalRRuntimeReplicaMonitoringSnapshotsAsync(SampleProcess host)
    {
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        var observed = new HashSet<int>();
        string? lastSnapshotJson = null;
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            observed.Clear();
            lastException = null;

            foreach (var replica in Enumerable.Range(1, 3))
            {
                var replicaResourceId = CreateSignalRReplicaResourceId(replica);
                try
                {
                    lastSnapshotJson = await host.GetStringAsync(
                        $"/api/control-plane/v1/resources/{Uri.EscapeDataString(replicaResourceId)}/monitoring");
                    using var snapshotDocument = JsonDocument.Parse(lastSnapshotJson);
                    var snapshot = snapshotDocument.RootElement;
                    if (string.Equals(
                            snapshot.GetProperty("resourceId").GetString(),
                            replicaResourceId,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(snapshot.GetProperty("status").GetString(), "Available", StringComparison.OrdinalIgnoreCase) &&
                        snapshot.GetProperty("metrics").GetArrayLength() > 0)
                    {
                        observed.Add(replica);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    lastException = exception;
                    break;
                }
            }

            if (observed.Count == 3)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"SignalR Docker runtime replica monitoring did not return metric snapshots for all replicas within {StartupTimeout}." +
            $"{Environment.NewLine}{lastSnapshotJson}{Environment.NewLine}{lastException?.Message}");
    }

    private static async Task AssertSignalRReplicaMonitoringMetricsFallbackAsync(SampleProcess host)
    {
        const string apiResourceId = "application.container-app:signalr-api";
        var metricsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(apiResourceId)}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Metrics.Value)}");

        Assert.Contains("Current monitoring snapshot", metricsHtml);
        Assert.Contains("resource.cpu.usage", metricsHtml);
        Assert.Contains("resource.process.count", metricsHtml);
    }

    private static async Task AssertSignalRReplicaResourceLinksAsync(SampleProcess host)
    {
        const string apiResourceId = "application.container-app:signalr-api";
        var replicaRoute = ResourceManagerRoutes.ResourceDetails(CreateSignalRReplicaResourceId(1));
        var scalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(apiResourceId)}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        var monitoringHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString(apiResourceId)}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value)}");

        Assert.Contains(replicaRoute, scalingHtml);
        Assert.Contains(replicaRoute, monitoringHtml);
    }

    private static async Task AssertSignalRReplicaLogSourcesAsync(SampleProcess host)
    {
        const string apiResourceId = "application.container-app:signalr-api";
        var runtimeDefinition = CreateSignalRRuntimeDefinition();
        var logSourcesJson = await host.GetStringAsync(
            $"/api/control-plane/v1/log-sources?resourceId={Uri.EscapeDataString(apiResourceId)}");
        using var logSourcesDocument = JsonDocument.Parse(logSourcesJson);
        var replicaSources = logSourcesDocument.RootElement
            .EnumerateArray()
            .Where(source =>
                source.TryGetProperty("producerResourceId", out var producer) &&
                producer.GetString()?.StartsWith(
                    runtimeDefinition.ReplicaResourceIdPrefix,
                    StringComparison.OrdinalIgnoreCase) == true)
            .OrderBy(source => source.GetProperty("id").GetString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(3, replicaSources.Length);
        for (var replica = 1; replica <= 3; replica++)
        {
            var source = replicaSources[replica - 1];
            var replicaResourceId = CreateSignalRReplicaResourceId(replica);
            Assert.Equal(
                $"{apiResourceId}:replica-{replica.ToString(CultureInfo.InvariantCulture)}:logs",
                source.GetProperty("id").GetString());
            Assert.Equal(replicaResourceId, source.GetProperty("producerResourceId").GetString());
            Assert.Equal(
                $"Replica {replica.ToString(CultureInfo.InvariantCulture)} logs",
                source.GetProperty("name").GetString());
            Assert.Equal(apiResourceId, source.GetProperty("resourceId").GetString());
            Assert.Equal((int)ResourceLogSourceKind.Container, source.GetProperty("kind").GetInt32());
            Assert.Equal((int)LogFormat.JsonConsole, source.GetProperty("format").GetInt32());
            Assert.Equal((int)ResourceLogSourceOrigin.ProviderProjected, source.GetProperty("origin").GetInt32());
            Assert.Equal((int)LogSourceAvailability.ProducerRunning, source.GetProperty("availability").GetInt32());
        }

        await WaitForAnyStructuredSignalRReplicaLogEntriesAsync(host, replicaSources);
    }

    private static LocalDockerContainerApplicationRuntimeDefinition CreateSignalRRuntimeDefinition() =>
        LocalDockerContainerApplicationRuntimeDefinition.CreateDefault("application.container-app:signalr-api");

    private static string CreateSignalRReplicaResourceId(int replica) =>
        LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(
            CreateSignalRRuntimeDefinition(),
            replica);

    private static async Task WaitForAnyStructuredSignalRReplicaLogEntriesAsync(
        SampleProcess host,
        IReadOnlyList<JsonElement> replicaSources)
    {
        var deadline = DateTimeOffset.UtcNow.Add(StartupTimeout);
        var lastResponses = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        do
        {
            foreach (var source in replicaSources)
            {
                var logSourceId = source.GetProperty("id").GetString() ??
                    throw new InvalidOperationException("The log source did not include an id.");
                var body = await host.GetStringAsync(
                    $"/api/control-plane/v1/log-sources/{Uri.EscapeDataString(logSourceId)}/entries?maxEntries=50");
                lastResponses[logSourceId] = body;
                using var document = JsonDocument.Parse(body);
                if (document.RootElement
                    .EnumerateArray()
                    .Any(IsStructuredJsonConsoleLogEntry))
                {
                    return;
                }
            }

            await Task.Delay(250);
        }
        while (DateTimeOffset.UtcNow < deadline);

        var lastResponseText = string.Join(
            Environment.NewLine,
            lastResponses.Select(response => $"{response.Key}: {response.Value}"));
        throw new TimeoutException(
            $"Timed out waiting for structured SignalR replica log entries. Last responses: {lastResponseText}");
    }

    private static bool IsStructuredJsonConsoleLogEntry(JsonElement entry)
    {
        if (!entry.TryGetProperty("message", out var messageProperty))
        {
            return false;
        }

        var message = messageProperty.GetString();
        return !string.IsNullOrWhiteSpace(message) &&
            !message.TrimStart().StartsWith('{') &&
            entry.TryGetProperty("category", out var category) &&
            !string.IsNullOrWhiteSpace(category.GetString());
    }

    [Fact]
    public async Task ReplicatedContainerHealthSample_DeclaresResourcesWithoutOldProviderRecords()
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
            resource.GetProperty("id").GetString() == "docker.host:sample");
        var graphApp = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:api");
        var graphAppAttributes = graphApp.GetProperty("attributes");

        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:api");
        Assert.Equal("docker.host", graphDocker.GetProperty("typeId").GetString());
        Assert.Equal("application.container-app", graphApp.GetProperty("typeId").GetString());
        Assert.Equal(
            "cloudshell-application-api:20260622.2",
            graphAppAttributes.GetProperty("container.image").GetString());
        Assert.Equal("3", graphAppAttributes.GetProperty("container.replicas").GetString());
        Assert.Equal("Cookie", graphAppAttributes.GetProperty("container.routing.sessionAffinity.mode").GetString());
        Assert.Equal("CloudShellReplica", graphAppAttributes.GetProperty("container.routing.sessionAffinity.cookieName").GetString());
        Assert.Equal("3600", graphAppAttributes.GetProperty("container.routing.sessionAffinity.durationSeconds").GetString());
        Assert.Equal("true", graphAppAttributes.GetProperty(ResourceAttributeNames.ContainerReplicasEnabled).GetString());
        Assert.Equal("3", graphAppAttributes.GetProperty(ResourceAttributeNames.DeploymentRequestedReplicaSlots).GetString());
        Assert.Equal(
            $"http://localhost:{apiPort.ToString(CultureInfo.InvariantCulture)}",
            GetPrimaryEndpointAddress(graphApp));
        Assert.Contains(
            "docker.host:sample",
            graphApp.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.HealthChecks.ToString());
        Assert.Contains(
            graphApp.GetProperty("capabilities").EnumerateArray(),
            capability => capability.GetProperty("id").GetString() == ResourceHealthCheckCapabilityIds.Liveness.ToString());

        var graphScalingHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:api")}/details?tab={Uri.EscapeDataString("application:scale-replicas")}");
        Assert.Contains("Scale and replicas", graphScalingHtml);
        Assert.Contains("Requested replica slots", graphScalingHtml);
        Assert.Contains("Replica slots", graphScalingHtml);
        Assert.Contains("Update replicas", graphScalingHtml);
        Assert.Contains("Session affinity", graphScalingHtml);
        Assert.Contains("Cookie", graphScalingHtml);
        Assert.Contains("Slot 1", graphScalingHtml);
        Assert.Contains("Slot 2", graphScalingHtml);
        Assert.Contains("Slot 3", graphScalingHtml);
        Assert.Contains(">3</dd>", graphScalingHtml);

        var graphDeploymentHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:api")}/details?tab={Uri.EscapeDataString("application:deployment")}");
        Assert.Contains("Deploy image", graphDeploymentHtml);
        Assert.Contains("Current image", graphDeploymentHtml);
        Assert.Contains("Replicated", graphDeploymentHtml);
        Assert.Contains("3 replica slots", graphDeploymentHtml);

        var graphRevisionsHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:api")}/details?tab={Uri.EscapeDataString("application:revisions")}");
        Assert.Contains("No revisions recorded", graphRevisionsHtml);

        var graphMonitoringHtml = await host.GetStringAsync(
            $"/resources/{Uri.EscapeDataString("application.container-app:api")}/details?tab={Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value)}");
        Assert.Contains("Monitoring", graphMonitoringHtml);
    }


    [Fact]
    [Trait("Category", "DockerIntegration")]
    public async Task ReplicatedContainerHealthSample_ImageRolloutAndReplicaUpdatesReconcileRuntime()
    {
        const string graphApiResourceId = "application.container-app:api";
        const string updatedImage = "cloudshell-application-api:20260622.3";
        string[] containerNames =
        [
            "cloudshell-replicated-health-api-replica-1",
            "cloudshell-replicated-health-api-replica-2",
            "cloudshell-replicated-health-api-replica-3"
        ];
        string[] scaledContainerNames =
        [
            "cloudshell-replicated-health-api-replica-1",
            "cloudshell-replicated-health-api-replica-2"
        ];
        const string ingressContainerName = "cloudshell-replicated-health-api-ingress";
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
                ("ReplicatedContainerHealth__RuntimeStatusCacheMilliseconds", "25"),
                ("ReplicatedContainerHealth__RuntimeReplicaCleanupLimit", "3")
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

            await StartGraphResourceIfAvailableAsync(host, graphApp, "ReplicatedContainerHealth API");
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
                message => message.Contains("reported healthy", StringComparison.OrdinalIgnoreCase));
            Assert.NotEmpty(graphReplicaLogEntries);
            await AssertGraphResourceHealthChecksHealthyAsync(host, graphApiResourceId, apiPort, graphOnlySmokeTimeout);
            await AssertGraphResourceRuntimeHealthAggregatesAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaRuntimeEnvironmentAsync(
                "cloudshell-replicated-health-api-replica-1",
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
            await AssertGraphReplicaChildrenProjectedAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaMonitoringSnapshotsAsync(
                host,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
            await AssertGraphMonitoringPanelReplicaSnapshotsAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
            await AssertGraphScalePanelReplicaOccupantsAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 3,
                graphOnlySmokeTimeout);
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
                throw new InvalidOperationException("The container image update action did not include an href.");
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
                    $"Expected Docker container '{containerName}' to be recreated after image update.");
            }
            var imageUpdatedContainerIds = await GetDockerContainerIdsAsync(containerNames);

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
            await AssertGraphReplicaChildrenProjectedAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 2,
                graphOnlySmokeTimeout);
            await AssertGraphReplicaMonitoringSnapshotsAsync(
                host,
                expectedReplicas: 2,
                graphOnlySmokeTimeout);
            await AssertGraphMonitoringPanelReplicaSnapshotsAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 2,
                graphOnlySmokeTimeout);
            await AssertGraphScalePanelReplicaOccupantsAsync(
                host,
                graphApiResourceId,
                expectedReplicas: 2,
                graphOnlySmokeTimeout);
            var scaledContainerIds = await GetDockerContainerIdsAsync(scaledContainerNames);
            foreach (var containerName in scaledContainerNames)
            {
                Assert.Equal(imageUpdatedContainerIds[containerName], scaledContainerIds[containerName]);
            }

            Assert.True(
                await WaitForDockerContainerRemovedAsync(
                    "cloudshell-replicated-health-api-replica-3",
                    graphOnlySmokeTimeout),
                "Expected stale replica 3 to be removed after scale-down.");

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
                "ReplicatedContainerHealth API");

            foreach (var containerName in runtimeContainerNames)
            {
                Assert.True(
                    await WaitForDockerContainerRemovedAsync(containerName, graphOnlySmokeTimeout),
                    $"Expected runtime container '{containerName}' to be removed after resource stop.");
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
    public async Task HostVirtualNetworkSample_ReconcilesEndpointMappingThroughRuntimeBridge()
    {
        const string apiResourceId = "application.dotnet-app:vnet-api";
        const string workerResourceId = "application.dotnet-app:vnet-worker";
        const string networkResourceId = "cloudshell.virtualNetwork:sample-vnet";
        const string dnsZoneResourceId = "cloudshell.dnsZone:sample-vnet-internal";
        var targetPort = await GetFreePortAsync();
        var workerTargetPort = await GetFreePortAsync();
        var virtualNetworkPort = await GetFreePortAsync();
        var coreDnsDirectory = Path.Combine(
            Path.GetTempPath(),
            $"cloudshell-coredns-{Guid.NewGuid():N}");
        using var host = await SampleProcess.StartAsync(
            "samples/HostVirtualNetwork/CloudShell.HostVirtualNetwork.csproj",
            await GetFreePortAsync(),
            [
                ("HostVirtualNetwork__TargetPort", targetPort.ToString(CultureInfo.InvariantCulture)),
                ("HostVirtualNetwork__WorkerTargetPort", workerTargetPort.ToString(CultureInfo.InvariantCulture)),
                ("HostVirtualNetwork__VirtualNetworkPort", virtualNetworkPort.ToString(CultureInfo.InvariantCulture)),
                ("HostVirtualNetwork__CoreDnsDirectory", coreDnsDirectory)
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);
        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        Assert.DoesNotContain(resources, resource =>
            resource.GetProperty("id").GetString() == "application:vnet-api");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == apiResourceId);
        var worker = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == workerResourceId);
        var network = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == networkResourceId);
        var dnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == dnsZoneResourceId);
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.nameMapping:api-internal");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.nameMapping:worker-internal");
        Assert.Equal("http://10.42.0.10:80", GetEndpointAddress(api, "vnet-http"));
        Assert.Equal("http://10.42.0.11:80", GetEndpointAddress(worker, "vnet-http"));

        var dnsReconcile = dnsZone
            .GetProperty("resourceActions")
            .GetProperty("reconcileNameMappings");
        var dnsHref = dnsReconcile.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The DNS zone reconcile action did not include an href.");
        var dnsJson = await host.SendAsync(HttpMethod.Post, dnsHref);
        using var dnsDocument = JsonDocument.Parse(dnsJson);
        Assert.Contains(
            "Published 2 CoreDNS host mapping(s)",
            dnsDocument.RootElement.GetProperty("message").GetString());
        var coreDnsHosts = await File.ReadAllTextAsync(Path.Combine(coreDnsDirectory, "cloudshell.hosts"));
        Assert.Contains("10.42.0.10 api.internal.cloudshell.test", coreDnsHosts);
        Assert.Contains("10.42.0.11 worker.internal.cloudshell.test", coreDnsHosts);
        var coreFile = await File.ReadAllTextAsync(Path.Combine(coreDnsDirectory, "Corefile"));
        Assert.Contains("hosts ", coreFile);
        Assert.Contains("cloudshell.hosts", coreFile);

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
            resource.GetProperty("id").GetString() == "cloudshell.loadBalancer:public");
        var dnsZone = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.dnsZone:cloudshell-local");
        var api = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:api");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.nameMapping:app-cloudshell-local");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.nameMapping:api-cloudshell-local");
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
            "docker.host:sample-host",
            api.GetProperty("dependsOn").EnumerateArray().Select(item => item.GetString()));
        Assert.Equal("cloudshell.loadBalancer", loadBalancer.GetProperty("typeId").GetString());
        Assert.Equal("traefik", loadBalancerAttributes.GetProperty("loadBalancer.provider").GetString());
        Assert.Equal("docker.host:sample-host", loadBalancerAttributes.GetProperty("loadBalancer.hostResourceId").GetString());
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

        var configPath = Path.Combine(dataDirectory, "traefik", "cloudshell-loadbalancer-public.dynamic.yml");
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

    [Fact]
    public async Task CertificateLoadBalancerSample_AppliesHttpsCertificateFromVault()
    {
        var root = SampleProcess.FindRepositoryRoot();
        var dataDirectory = Path.Combine(root, "samples", "CertificateLoadBalancer", "Data");
        if (Directory.Exists(dataDirectory))
        {
            Directory.Delete(dataDirectory, recursive: true);
        }

        using var host = await SampleProcess.StartAsync(
            "samples/CertificateLoadBalancer/CloudShell.CertificateLoadBalancer.csproj",
            await GetFreePortAsync(),
            [
                ("CLOUDSHELL_CERTIFICATE_LOADBALANCER_SKIP_TRAEFIK_RUNTIME", "true")
            ]);

        await host.WaitForHttpOkAsync("/", StartupTimeout);

        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var resources = resourcesDocument.RootElement.EnumerateArray().ToArray();
        var loadBalancer = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.loadBalancer:public");
        var vault = Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "secrets.vault:edge-certificates");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "application.container-app:web");
        Assert.Single(resources, resource =>
            resource.GetProperty("id").GetString() == "cloudshell.nameMapping:secure-cloudshell-local");

        var vaultAttributes = vault.GetProperty("attributes");
        var loadBalancerAttributes = loadBalancer.GetProperty("attributes");
        Assert.Equal("1", vaultAttributes.GetProperty("certificateCount").GetString());
        Assert.Equal("1", loadBalancerAttributes.GetProperty("loadBalancer.routes").GetString());
        Assert.Equal("1", loadBalancerAttributes.GetProperty("loadBalancer.routes.http").GetString());

        var applyAction = loadBalancer
            .GetProperty("resourceActions")
            .GetProperty("applyLoadBalancerConfiguration");
        var applyHref = applyAction.GetProperty("href").GetString() ??
            throw new InvalidOperationException("The load balancer apply action did not include an href.");
        var applyJson = await host.SendAsync(HttpMethod.Post, applyHref);
        using var applyDocument = JsonDocument.Parse(applyJson);
        Assert.Contains(
            "Applied Traefik configuration for 1 route(s)",
            applyDocument.RootElement.GetProperty("message").GetString());

        var configPath = Path.Combine(dataDirectory, "traefik", "cloudshell-loadbalancer-public.dynamic.yml");
        var certificatePath = Path.Combine(dataDirectory, "traefik", "certificates", "cloudshell-loadbalancer-public-https.crt");
        var keyPath = Path.Combine(dataDirectory, "traefik", "certificates", "cloudshell-loadbalancer-public-https.key");
        var config = await File.ReadAllTextAsync(configPath);
        var certificate = await File.ReadAllTextAsync(certificatePath);
        var key = await File.ReadAllTextAsync(keyPath);
        Assert.Contains("entryPoints: [\"https\"]", config);
        Assert.Contains("Host(`secure.cloudshell.local`)", config);
        Assert.Contains("tls: {}", config);
        Assert.Contains($"certFile: \"{certificatePath}\"", config);
        Assert.Contains($"keyFile: \"{keyPath}\"", config);
        Assert.Contains("BEGIN CERTIFICATE", certificate);
        Assert.DoesNotContain("BEGIN PRIVATE KEY", certificate);
        Assert.Contains("BEGIN PRIVATE KEY", key);
        Assert.DoesNotContain("BEGIN CERTIFICATE", key);
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
            environment.Add(("ApplicationTopology__ConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ApplicationTopology__SecretsServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
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
            environment.Add(("ProjectReference__ApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "DeviceRegistry")
        {
            environment.Add(("Samples__DeviceRegistry__RegistryEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__DeviceRegistry__SecretsEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__DeviceRegistry__ConfigurationEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__DeviceRegistry__MqttEndpoint", $"mqtt://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__DeviceRegistry__EventBrokerMqttEndpoint", $"mqtt://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__DeviceRegistry__EventBrokerHttpEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "RabbitMQMessaging")
        {
            environment.Add(("RabbitMQMessaging__DotNetEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("RabbitMQMessaging__JavaEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("RabbitMQMessaging__RabbitMQPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("RabbitMQMessaging__ManagementEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "ReplicatedContainerHealth")
        {
            environment.Add(("ReplicatedContainerHealth__ApiPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("ReplicatedContainerHealth__RuntimeStatusCacheMilliseconds", "25"));
        }
        else if (sampleName == "SignalRContainerApp")
        {
            environment.Add(("SignalRContainerApp__ApiPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("SignalRContainerApp__FrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "RoboticMowerIoT")
        {
            var registryPort = await GetFreePortAsync();
            environment.Add(("RoboticMowerIoT__BackendPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
            environment.Add(("RoboticMowerIoT__FrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("RoboticMowerIoT__DeviceRegistryEndpoint", $"http://localhost:{registryPort}"));
            environment.Add(("RoboticMowerIoT__BackendDeviceRegistryEndpoint", $"http://host.docker.internal:{registryPort}"));
            environment.Add(("RoboticMowerIoT__DeviceRegistryMqttEndpoint", $"mqtt://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "SettingsAndSecrets")
        {
            environment.Add(("Samples__SettingsAndSecrets__ConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__SettingsAndSecrets__SecretsServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("Samples__SettingsAndSecrets__ApiEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
        }
        else if (sampleName == "PythonAppHost")
        {
            environment.Add(("Authentication__Enabled", "false"));
        }
        else if (sampleName == "ReactTypeScriptApp")
        {
            environment.Add(("Authentication__Enabled", "false"));
            environment.Add(("ReactTypeScriptApp__FrontendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ReactTypeScriptApp__BackendEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ReactTypeScriptApp__ConfigurationServiceEndpoint", $"http://localhost:{await GetFreePortAsync()}"));
            environment.Add(("ReactTypeScriptApp__EdgeHttpPort", (await GetFreePortAsync()).ToString(CultureInfo.InvariantCulture)));
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

    private static bool IsLauncherSampleProject(string projectPath) =>
        projectPath.Contains("/AppHost/", StringComparison.OrdinalIgnoreCase);

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

        if (projectPath.Contains("/DeviceRegistry/", StringComparison.OrdinalIgnoreCase))
        {
            return "DeviceRegistry";
        }

        if (projectPath.Contains("/RabbitMQMessaging/", StringComparison.OrdinalIgnoreCase))
        {
            return "RabbitMQMessaging";
        }

        if (projectPath.Contains("/ReplicatedContainerHealth/", StringComparison.OrdinalIgnoreCase))
        {
            return "ReplicatedContainerHealth";
        }

        if (projectPath.Contains("/SignalRContainerApp/", StringComparison.OrdinalIgnoreCase))
        {
            return "SignalRContainerApp";
        }

        if (projectPath.Contains("/RoboticMowerIoT/", StringComparison.OrdinalIgnoreCase))
        {
            return "RoboticMowerIoT";
        }

        if (projectPath.Contains("/SettingsAndSecrets/", StringComparison.OrdinalIgnoreCase))
        {
            return "SettingsAndSecrets";
        }

        if (projectPath.Contains("/PythonAppHost/", StringComparison.OrdinalIgnoreCase))
        {
            return "PythonAppHost";
        }

        if (projectPath.Contains("/ReactTypeScriptApp/", StringComparison.OrdinalIgnoreCase))
        {
            return "ReactTypeScriptApp";
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
            "RabbitMQMessaging" =>
            [
                "application:rabbitmq",
                "application:rabbitmq-dotnet",
                "application:rabbitmq-java"
            ],
            "ReplicatedContainerHealth" =>
            [
                "application:api"
            ],
            "SignalRContainerApp" =>
            [
                "application:signalr-api",
                "application:signalr-frontend"
            ],
            "RoboticMowerIoT" =>
            [
                "application:mower-backend",
                "application:mower-frontend",
                "iot:park-devices"
            ],
            "SettingsAndSecrets" =>
            [
                "configuration:sample-app",
                "secrets-vault:sample-app",
                "application:settings-secrets-api"
            ],
            "ReactTypeScriptApp" =>
            [
                "configuration:react-typescript-settings",
                "application:react-api",
                "application:react-frontend"
            ],
            "SplitHosting" => [],
            "ThirdPartyIdentity" =>
            [
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
                "cloudshell-container-host-sql-server");
        }
        else if (sampleName == "ContainerAppDeployment")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                "cloudshell-container-app-deployment-registry");
        }
        else if (sampleName == "ApplicationTopology")
        {
            await DockerComposeStack.RemoveContainerIfExistsAsync(
                "cloudshell-application-topology-sql-server");
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
                "cloudshell-replicated-health-api-ingress");

            for (var replica = 1; replica <= 10; replica++)
            {
                await DockerComposeStack.RemoveContainerIfExistsAsync(
                    $"cloudshell-replicated-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}");
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

    private static async Task<IReadOnlyList<int>> GetFreePortRangeAsync(int count)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        for (var attempt = 0; attempt < 100; attempt++)
        {
            var start = await GetFreePortAsync();
            if (start + count >= IPEndPoint.MaxPort)
            {
                continue;
            }

            var listeners = new List<TcpListener>(count);
            try
            {
                for (var offset = 0; offset < count; offset++)
                {
                    var listener = new TcpListener(IPAddress.Loopback, start + offset);
                    listener.Start();
                    listeners.Add(listener);
                }

                return Enumerable.Range(start, count).ToArray();
            }
            catch (SocketException)
            {
            }
            finally
            {
                foreach (var listener in listeners)
                {
                    listener.Stop();
                }
            }
        }

        throw new InvalidOperationException(
            $"Could not find {count.ToString(CultureInfo.InvariantCulture)} contiguous free TCP ports.");
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
            $"Timed out waiting for runtime replica log entries for resource '{resourceId}'. Last responses: {lastResponseText}");
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
        string? lastUnfilteredBody = null;

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

        try
        {
            lastUnfilteredBody = await host.GetStringAsync(
                "/api/control-plane/v1/metrics?maxPoints=50");
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            lastException = exception;
        }

        throw new TimeoutException(
            $"Metrics for resource '{resourceId}' were not ingested within {timeout}." +
            $"{Environment.NewLine}Filtered: {lastBody ?? lastException?.Message}" +
            $"{Environment.NewLine}Unfiltered: {lastUnfilteredBody ?? lastException?.Message}");
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
            var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
            using var resourcesDocument = JsonDocument.Parse(resourcesJson);
            var resource = resourcesDocument.RootElement
                .EnumerateArray()
                .FirstOrDefault(candidate =>
                    string.Equals(
                        candidate.GetProperty("id").GetString(),
                        resourceId,
                        StringComparison.OrdinalIgnoreCase));
            if (resource.ValueKind != JsonValueKind.Undefined &&
                resource.TryGetProperty("state", out var state) &&
                state.ValueKind == JsonValueKind.Number &&
                state.GetInt32() == (int)ResourceState.Stopped)
            {
                return;
            }

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

    private static async Task AssertGraphReplicaChildrenProjectedAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? lastChildrenJson = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastChildrenJson = await host.GetStringAsync(
                $"/api/control-plane/v1/resources/{Uri.EscapeDataString(resourceId)}/children");
            using var childrenDocument = JsonDocument.Parse(lastChildrenJson);
            var replicas = childrenDocument.RootElement
                .EnumerateArray()
                .Where(IsRuntimeReplicaChild)
                .OrderBy(resource => int.Parse(
                    resource.GetProperty("attributes").GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString()!,
                    CultureInfo.InvariantCulture))
                .ToArray();

            if (replicas.Length == expectedReplicas &&
                Enumerable.Range(1, expectedReplicas).All(replica =>
                    replicas.Any(resource => IsExpectedRuntimeReplicaChild(resource, replica, expectedReplicas))))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Graph resource '{resourceId}' did not project {expectedReplicas.ToString(CultureInfo.InvariantCulture)} runtime replica child resource(s) within {timeout}." +
            $"{Environment.NewLine}{lastChildrenJson}");
    }

    private static async Task AssertGraphScalePanelReplicaOccupantsAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas,
        TimeSpan timeout)
    {
        var tab = Uri.EscapeDataString("application:scale-replicas");
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? lastHtml = null;
        while (DateTimeOffset.UtcNow < deadline)
        {
            lastHtml = await host.GetStringAsync(
                $"/resources/{Uri.EscapeDataString(resourceId)}/details?tab={tab}");
            if (lastHtml.Contains("Occupied slots", StringComparison.OrdinalIgnoreCase) &&
                lastHtml.Contains($">{expectedReplicas.ToString(CultureInfo.InvariantCulture)}</dd>", StringComparison.OrdinalIgnoreCase) &&
                Enumerable.Range(1, expectedReplicas).All(replica =>
                    lastHtml.Contains($"api replica {replica.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase) &&
                    lastHtml.Contains(
                        LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(replica),
                        StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Graph scale panel did not show {expectedReplicas.ToString(CultureInfo.InvariantCulture)} occupied replica slot(s) for '{resourceId}' within {timeout}." +
            $"{Environment.NewLine}{lastHtml}");
    }

    private static async Task AssertGraphReplicaMonitoringSnapshotsAsync(
        SampleProcess host,
        int expectedReplicas,
        TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        var observed = new HashSet<int>();
        string? lastSnapshotJson = null;
        Exception? lastException = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            observed.Clear();
            lastException = null;

            foreach (var replica in Enumerable.Range(1, expectedReplicas))
            {
                var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
                try
                {
                    lastSnapshotJson = await host.GetStringAsync(
                        $"/api/control-plane/v1/resources/{Uri.EscapeDataString(replicaResourceId)}/monitoring");
                    using var snapshotDocument = JsonDocument.Parse(lastSnapshotJson);
                    var snapshot = snapshotDocument.RootElement;
                    if (string.Equals(
                            snapshot.GetProperty("resourceId").GetString(),
                            replicaResourceId,
                            StringComparison.OrdinalIgnoreCase) &&
                        snapshot.GetProperty("metrics").GetArrayLength() > 0)
                    {
                        observed.Add(replica);
                    }
                }
                catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
                {
                    lastException = exception;
                    break;
                }
            }

            if (observed.Count == expectedReplicas)
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Runtime replica monitoring did not return metric snapshots for {expectedReplicas.ToString(CultureInfo.InvariantCulture)} replica(s) within {timeout}." +
            $"{Environment.NewLine}{lastSnapshotJson}{Environment.NewLine}{lastException?.Message}");
    }

    private static async Task AssertGraphMonitoringPanelReplicaSnapshotsAsync(
        SampleProcess host,
        string resourceId,
        int expectedReplicas,
        TimeSpan timeout)
    {
        var tab = Uri.EscapeDataString(ResourcePredefinedViewIds.Monitoring.Value);
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        string? lastHtml = null;

        while (DateTimeOffset.UtcNow < deadline)
        {
            lastHtml = await host.GetStringAsync(
                $"/resources/{Uri.EscapeDataString(resourceId)}/details?tab={tab}");

            if (lastHtml.Contains(
                    $"{expectedReplicas.ToString(CultureInfo.InvariantCulture)} of {expectedReplicas.ToString(CultureInfo.InvariantCulture)} replicas observed",
                    StringComparison.OrdinalIgnoreCase) &&
                Enumerable.Range(1, expectedReplicas).All(replica =>
                    lastHtml.Contains($"api replica {replica.ToString(CultureInfo.InvariantCulture)}", StringComparison.OrdinalIgnoreCase)) &&
                !lastHtml.Contains("No snapshot", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            await Task.Delay(250);
        }

        throw new TimeoutException(
            $"Graph monitoring panel did not show {expectedReplicas.ToString(CultureInfo.InvariantCulture)} observed runtime replica snapshot(s) for '{resourceId}' within {timeout}." +
            $"{Environment.NewLine}{lastHtml}");
    }

    private static bool IsRuntimeReplicaChild(JsonElement resource) =>
        string.Equals(resource.GetProperty("typeId").GetString(), "runtime.container", StringComparison.OrdinalIgnoreCase) &&
        resource.TryGetProperty("attributes", out var attributes) &&
        attributes.TryGetProperty(ResourceAttributeNames.RuntimeKind, out var runtimeKind) &&
        string.Equals(runtimeKind.GetString(), "containerReplica", StringComparison.OrdinalIgnoreCase);

    private static bool IsExpectedRuntimeReplicaChild(
        JsonElement resource,
        int replica,
        int replicaCount)
    {
        var replicaText = replica.ToString(CultureInfo.InvariantCulture);
        var replicaCountText = replicaCount.ToString(CultureInfo.InvariantCulture);
        var attributes = resource.GetProperty("attributes");

        return string.Equals(
                resource.GetProperty("id").GetString(),
                LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(
                attributes.GetProperty(ResourceAttributeNames.RuntimeContainerName).GetString(),
                LocalDockerContainerApplicationRuntimeConventions.CreateReplicaContainerName(replica),
                StringComparison.OrdinalIgnoreCase) &&
            attributes.GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString() == replicaText &&
            attributes.GetProperty(ResourceAttributeNames.RuntimeReplicaCount).GetString() == replicaCountText;
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
                        LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica),
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
        var resourcesJson = await host.GetStringAsync("/api/control-plane/v1/resources");
        using var resourcesDocument = JsonDocument.Parse(resourcesJson);
        var replicaResourceIdsByOrdinal = resourcesDocument.RootElement
            .EnumerateArray()
            .Where(resource =>
                string.Equals(resource.GetProperty("typeId").GetString(), "runtime.container", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(resource.GetProperty("ownerResourceId").GetString(), resourceId, StringComparison.OrdinalIgnoreCase) &&
                resource.TryGetProperty("attributes", out var attributes) &&
                attributes.TryGetProperty(ResourceAttributeNames.RuntimeKind, out var runtimeKind) &&
                string.Equals(runtimeKind.GetString(), "containerReplica", StringComparison.OrdinalIgnoreCase) &&
                attributes.TryGetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal, out _))
            .ToDictionary(
                resource => resource.GetProperty("attributes").GetProperty(ResourceAttributeNames.RuntimeReplicaOrdinal).GetString()!,
                resource => resource.GetProperty("id").GetString()!,
                StringComparer.OrdinalIgnoreCase);
        var replicaResourceIds = replicaResourceIdsByOrdinal.Values.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var logSourcesJson = await host.GetStringAsync(
            $"/api/control-plane/v1/log-sources?resourceId={Uri.EscapeDataString(resourceId)}");
        using var logSourcesDocument = JsonDocument.Parse(logSourcesJson);
        var sources = logSourcesDocument.RootElement
            .EnumerateArray()
            .Where(source =>
                source.TryGetProperty("producerResourceId", out var producer) &&
                producer.GetString() is { } producerResourceId &&
                replicaResourceIds.Contains(producerResourceId))
            .OrderBy(source => source.GetProperty("name").GetString(), StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(expectedReplicas, sources.Length);
        for (var replica = 1; replica <= expectedReplicas; replica++)
        {
            var replicaText = replica.ToString(CultureInfo.InvariantCulture);
            var source = sources[replica - 1];
            Assert.Equal(
                $"{resourceId}:replica-{replicaText}:logs",
                source.GetProperty("id").GetString());
            Assert.Equal(
                $"Replica {replicaText} logs",
                source.GetProperty("name").GetString());
            Assert.Equal(resourceId, source.GetProperty("resourceId").GetString());
            Assert.True(replicaResourceIdsByOrdinal.TryGetValue(replicaText, out var replicaResourceId));
            Assert.Equal(
                replicaResourceId,
                source.GetProperty("producerResourceId").GetString());
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
        var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
        var environment = await DockerComposeStack.GetContainerEnvironmentAsync(containerName);
        Assert.Contains($"CLOUDSHELL_RESOURCE_ID={replicaResourceId}", environment);
        Assert.Contains($"CLOUDSHELL_REPLICA_ORDINAL={replica.ToString(CultureInfo.InvariantCulture)}", environment);
        Assert.Contains(
            $"OTEL_SERVICE_NAME=replicated-container-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
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
        Assert.Contains($"telemetry.scope.resourceId={LocalDockerContainerApplicationRuntimeConventions.ApiResourceId}", resourceAttributes);
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
        var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
        var metrics = await WaitForMetricPointsAsync(
            host,
            replicaResourceId,
            timeout,
            points => points.Any(point =>
                point.GetProperty("name").GetString() == "http.server.requests" &&
                point.GetProperty("resourceId").GetString() == replicaResourceId &&
                point.TryGetProperty("attributes", out var attributes) &&
                attributes.TryGetProperty("telemetry.scope.resourceId", out var scopeResourceId) &&
                scopeResourceId.GetString() == LocalDockerContainerApplicationRuntimeConventions.ApiResourceId));
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
                var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
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
            $"Runtime replica work trace was not ingested by any of {expectedReplicas.ToString(CultureInfo.InvariantCulture)} replica(s)." +
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

        return scopeResourceId.GetString() == LocalDockerContainerApplicationRuntimeConventions.ApiResourceId &&
            replicaOrdinal.GetString() == replica.ToString(CultureInfo.InvariantCulture);
    }

    private static async Task AssertGraphReplicaResourceObservabilityAsync(
        SampleProcess host,
        int replica)
    {
        var replicaResourceId = LocalDockerContainerApplicationRuntimeConventions.CreateReplicaResourceId(replica);
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
            $"replicated-container-health-api-replica-{replica.ToString(CultureInfo.InvariantCulture)}",
            observability.GetProperty("serviceName").GetString());

        var attributes = observability.GetProperty("attributes");
        var expectedRevisionId = ContainerApplicationRuntimeRevisions.CreateImageRevisionId(
            ContainerRegistryDefaults.Default,
            "cloudshell-application-api:20260622.2");
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            attributes.GetProperty("telemetry.scope.resourceId").GetString());
        Assert.Equal(
            replica.ToString(CultureInfo.InvariantCulture),
            attributes.GetProperty("runtime.replica.ordinal").GetString());
        Assert.Equal(
            expectedRevisionId,
            attributes.GetProperty("deployment.revision").GetString());

        var scope = Assert.Single(observability.GetProperty("scopes").EnumerateArray());
        Assert.Equal(
            LocalDockerContainerApplicationRuntimeConventions.ApiResourceId,
            scope.GetProperty("scopeResourceId").GetString());
        Assert.Equal($"Replica {replica.ToString(CultureInfo.InvariantCulture)}", scope.GetProperty("name").GetString());
        Assert.Equal("runtime", scope.GetProperty("kind").GetString());
        Assert.Equal(expectedRevisionId, scope.GetProperty("deploymentRevision").GetString());
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
        private static readonly TimeSpan DockerCleanupTimeout = TimeSpan.FromSeconds(5);
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
                    DockerCleanupTimeout,
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
                DockerCleanupTimeout,
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
        private readonly string? cleanupDirectory;

        private SampleProcess(
            Process process,
            Uri baseAddress,
            string? cleanupDirectory = null)
        {
            this.process = process;
            BaseAddress = baseAddress;
            this.cleanupDirectory = cleanupDirectory;
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

        public static async Task<SampleProcess> StartLauncherAsync(
            string projectPath,
            int port,
            IReadOnlyList<(string Key, string Value)>? environment = null)
        {
            var root = FindRepositoryRoot();
            var launcherPath = Path.Combine(root, projectPath);
            var projectDirectory = Path.GetDirectoryName(launcherPath) ??
                throw new InvalidOperationException($"Could not resolve sample project directory for '{projectPath}'.");
            var stateDirectory = Path.Combine(
                Path.GetTempPath(),
                $"cloudshell-sample-{Path.GetFileNameWithoutExtension(launcherPath)}-{Guid.NewGuid():N}");

            var baseAddress = new Uri($"http://127.0.0.1:{port}");
            var isPythonLauncher = string.Equals(
                Path.GetExtension(launcherPath),
                ".py",
                StringComparison.OrdinalIgnoreCase);
            var isTypeScriptPackageLauncher = string.Equals(
                Path.GetFileName(launcherPath),
                "package.json",
                StringComparison.OrdinalIgnoreCase);
            var startInfo = new ProcessStartInfo(
                isPythonLauncher
                    ? "python3"
                    : isTypeScriptPackageLauncher
                        ? "npm"
                        : "dotnet")
            {
                WorkingDirectory = isPythonLauncher || isTypeScriptPackageLauncher
                    ? projectDirectory
                    : root,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false
            };
            if (isPythonLauncher)
            {
                startInfo.ArgumentList.Add(launcherPath);
                startInfo.ArgumentList.Add("run");
                startInfo.Environment["PYTHONPATH"] = BuildPythonLauncherPath(root);
            }
            else if (isTypeScriptPackageLauncher)
            {
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("apply");
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add("--run");
            }
            else
            {
                startInfo.ArgumentList.Add("run");
                startInfo.ArgumentList.Add("--no-build");
                startInfo.ArgumentList.Add("--project");
                startInfo.ArgumentList.Add(launcherPath);
                startInfo.ArgumentList.Add("--");
                startInfo.ArgumentList.Add("--run");
            }

            startInfo.ArgumentList.Add("--no-build");
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
            startInfo.Environment["CLOUDSHELL_CONTROL_PLANE_URL"] = baseAddress.ToString().TrimEnd('/');
            startInfo.Environment["CLOUDSHELL_STATE_DIR"] = stateDirectory;
            startInfo.Environment["CLOUDSHELL_DATA_DIR"] = stateDirectory;

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            var process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Could not start sample launcher '{projectPath}'.");
            var sample = new SampleProcess(process, baseAddress, stateDirectory);
            sample.Capture(process.StandardOutput);
            sample.Capture(process.StandardError);
            try
            {
                await sample.WaitForResourcesAsync(
                    GetLauncherSampleResourceIds(projectPath),
                    SampleHostLaunchTimeout);
                return sample;
            }
            catch
            {
                sample.Dispose();
                throw;
            }
        }

        public static async Task<string> RunCSharpLauncherTemplateAsync(
            string projectPath,
            IReadOnlyList<(string Key, string Value)>? environment = null)
        {
            var root = FindRepositoryRoot();
            var launcherPath = Path.Combine(root, projectPath);
            var output = new StringBuilder();
            var error = new StringBuilder();
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
            startInfo.ArgumentList.Add(launcherPath);
            startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";

            if (environment is not null)
            {
                foreach (var (key, value) in environment)
                {
                    startInfo.Environment[key] = value;
                }
            }

            using var process = Process.Start(startInfo) ??
                throw new InvalidOperationException($"Could not start sample launcher '{projectPath}'.");
            var outputTask = CaptureAsync(process.StandardOutput, output);
            var errorTask = CaptureAsync(process.StandardError, error);
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
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
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    $"Sample launcher '{projectPath}' failed with exit code {process.ExitCode}." +
                    $"{Environment.NewLine}{output}{error}");
            }

            return output.ToString();

            static async Task CaptureAsync(StreamReader reader, StringBuilder builder)
            {
                while (await reader.ReadLineAsync() is { } line)
                {
                    builder.AppendLine(line);
                }
            }
        }

        private static string BuildPythonLauncherPath(string root)
        {
            var launcherPath = Path.Combine(root, "Launchers", "Python", "cloudshell");
            var existing = Environment.GetEnvironmentVariable("PYTHONPATH");
            return string.IsNullOrWhiteSpace(existing)
                ? launcherPath
                : $"{launcherPath}{Path.PathSeparator}{existing}";
        }

        private async Task WaitForResourcesAsync(
            IReadOnlyList<string> expectedResourceIds,
            TimeSpan timeout)
        {
            var deadline = DateTimeOffset.UtcNow.Add(timeout);
            string? lastBody = null;
            Exception? lastException = null;

            while (DateTimeOffset.UtcNow < deadline)
            {
                if (process.HasExited)
                {
                    throw new InvalidOperationException(
                        $"Sample process exited with code {process.ExitCode} before resources were applied.{Environment.NewLine}{GetOutput()}");
                }

                try
                {
                    lastBody = await GetStringAsync("/api/control-plane/v1/resources");
                    using var document = JsonDocument.Parse(lastBody);
                    if (document.RootElement.ValueKind == JsonValueKind.Array)
                    {
                        var resourceIds = document.RootElement
                            .EnumerateArray()
                            .Select(resource => resource.GetProperty("id").GetString())
                            .Where(resourceId => !string.IsNullOrWhiteSpace(resourceId))
                            .Select(resourceId => resourceId!)
                            .ToHashSet(StringComparer.OrdinalIgnoreCase);
                        if (expectedResourceIds.All(resourceIds.Contains))
                        {
                            return;
                        }
                    }
                }
                catch (Exception exception) when (exception is InvalidOperationException or HttpRequestException or TaskCanceledException or JsonException)
                {
                    lastException = exception;
                }

                await Task.Delay(250);
            }

            throw new TimeoutException(
                $"Sample launcher did not apply resources within {timeout}." +
                $"{Environment.NewLine}{lastBody ?? lastException?.Message}{Environment.NewLine}{GetOutput()}");
        }

        private static IReadOnlyList<string> GetLauncherSampleResourceIds(string projectPath)
        {
            if (projectPath.Contains("/ProjectReference/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "application.dotnet-app:project-reference-api",
                    "application.dotnet-app:project-reference-frontend"
                ];
            }

            if (projectPath.Contains("/JavaScriptApp/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:javascript-app-settings",
                    "application.javascript-app:javascript-frontend"
                ];
            }

            if (projectPath.Contains("/DeviceRegistry/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:device-settings",
                    "event.broker:events",
                    "secrets.vault:factory",
                    "iot.device-registry:devices"
                ];
            }

            if (projectPath.Contains("/JavaApp/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:java-app-settings",
                    "secrets.vault:java-app-secrets",
                    "application.java-app:java-api"
                ];
            }

            if (projectPath.Contains("/RabbitMQMessaging/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "cloudshell.volume:rabbitmq-messaging-data",
                    "application.rabbitmq:rabbitmq",
                    "application.dotnet-app:rabbitmq-dotnet",
                    "application.java-app:rabbitmq-java"
                ];
            }

            if (projectPath.Contains("/RoboticMowerIoT/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "cloudshell.container-host:default",
                    "network:host",
                    "application.container-app:mower-backend",
                    "application.javascript-app:mower-frontend",
                    "iot.device-registry:park-devices"
                ];
            }

            if (projectPath.Contains("/GoApp/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:go-app-settings",
                    "secrets.vault:go-app-secrets",
                    "application.go-app:go-api"
                ];
            }

            if (projectPath.Contains("/PythonAppHost/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:python-app-settings",
                    "secrets.vault:python-app-secrets",
                    "application.python-app:python-api"
                ];
            }

            if (projectPath.Contains("/ReactTypeScriptApp/", StringComparison.OrdinalIgnoreCase))
            {
                return
                [
                    "configuration.store:react-typescript-settings",
                    "application.javascript-app:react-api",
                    "application.javascript-app:react-frontend",
                    "cloudshell.loadBalancer:react-edge"
                ];
            }

            return [];
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
                Timeout = StartupTimeout
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
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(
                response.IsSuccessStatusCode,
                $"{method} {path} returned {(int)response.StatusCode} {response.ReasonPhrase}: {body}");
            return body;
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
            if (!string.IsNullOrWhiteSpace(cleanupDirectory) &&
                Directory.Exists(cleanupDirectory))
            {
                Directory.Delete(cleanupDirectory, recursive: true);
            }
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
                if (File.Exists(Path.Combine(directory.FullName, "CloudShell.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new InvalidOperationException("Could not find repository root.");
        }
    }
}
