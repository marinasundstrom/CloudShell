using CloudShell.Abstractions.Hosting;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionGraphBuilderTests
{
    [Fact]
    public void ResourceDefinitionGraphBuilder_DefineResourcesGroupsResourceDeclarations()
    {
        var graph = new ResourceDefinitionGraphBuilder()
            .DefineResources(resources =>
            {
                resources
                    .AddNetwork("app")
                    .WithDisplayName("App Network");
                resources
                    .AddConfigurationStore("settings")
                    .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries");
            });

        var graphDefinition = graph.BuildGraph();

        Assert.Equal(2, graphDefinition.Resources.Count);
        Assert.Contains(graphDefinition.Resources, resource =>
            resource.TypeId == NetworkResourceTypeProvider.ResourceTypeId &&
            resource.DisplayName == "App Network");
        Assert.Contains(graphDefinition.Resources, resource =>
            resource.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId &&
            resource.EffectiveResourceId == "configuration.store:settings");
    }

    [Fact]
    public void ResourceDefinitionGraphBuilder_BuildDeploymentProjectsGraphIntoDeploymentEnvelope()
    {
        var graph = new ResourceDefinitionGraphBuilder()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });

        var deployment = graph.BuildDeployment(
            "grouped",
            environmentId: "local",
            metadata: new Dictionary<string, string>
            {
                ["source"] = "test"
            });
        var definition = Assert.Single(deployment.Resources);

        Assert.Equal("grouped", deployment.Name);
        Assert.Equal("local", deployment.EnvironmentId);
        Assert.NotNull(deployment.Metadata);
        Assert.Equal("test", deployment.Metadata["source"]);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, definition.TypeId);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
        Assert.Equal("cloudshell.network:app", definition.ResourceId);
    }

    [Fact]
    public void ResourceDefinitionGraphBuilder_BuildGraphAssignsResourceIdsByConvention()
    {
        var graph = new ResourceDefinitionGraphBuilder()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });

        var definition = Assert.Single(graph.BuildGraph().Resources);

        Assert.Equal("cloudshell.network:app", definition.ResourceId);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
    }

    [Fact]
    public void ResourceDefinitionGraphBuilder_BuildsConfigurationPayloadFromNativeBuilderApi()
    {
        var graph = new ResourceDefinitionGraphBuilder()
            .DefineResources(resources =>
            {
                resources
                    .AddNetwork("app")
                    .WithConfiguration(
                        "network",
                        new TestConfiguration("10.0.0.0/24", Enabled: true));
            });

        var definition = Assert.Single(graph.BuildGraph().Resources);
        var configuration = definition.GetConfiguration<TestConfiguration>("network");

        Assert.NotNull(configuration);
        Assert.Equal("10.0.0.0/24", configuration!.AddressPrefix);
        Assert.True(configuration.Enabled);
    }

    [Fact]
    public void ResourceDefinitionGraphBuilder_UsesConfiguredResourceIdConventionForReferences()
    {
        var graph = new ResourceDefinitionGraphBuilder(new TestResourceIdConvention("host"))
            .DefineResources(resources =>
            {
                var docker = resources.AddDockerHost("sample");
                resources
                    .AddContainerApplication("api")
                    .UseDockerHost(docker);
            });

        var definitions = graph.BuildGraph().Resources;
        var dockerDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == DockerHostResourceTypeProvider.ResourceTypeId);
        var appDefinition = Assert.Single(
            definitions,
            resource => resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId);

        Assert.Equal("host/docker.host/sample", dockerDefinition.ResourceId);
        Assert.Equal("host/application.container-app/api", appDefinition.ResourceId);

        var dependency = Assert.Single(appDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyResourceId));
        Assert.Equal(dockerDefinition.ResourceId, dependencyResourceId);
    }

    [Fact]
    public async Task Host_DefineResourcesRegistersImplicitInitialGraph()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineResources(resources =>
            {
                resources.AddNetwork("app");
            });
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, resource.TypeId);
        Assert.Equal("cloudshell.network:app", resource.EffectiveResourceId);
    }

    [Fact]
    public async Task Host_DefineResourcesUsesConfiguredResourceIdConvention()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineResources(
                resources =>
                {
                    resources.AddNetwork("app");
                },
                resourceIdConvention: new TestResourceIdConvention("host"));
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal("host/cloudshell.network/app", resource.EffectiveResourceId);
    }

    [Fact]
    public async Task Host_DefineInitialDeploymentRegistersDeploymentInitialGraph()
    {
        var services = new ServiceCollection();
        services
            .AddCloudShellControlPlane()
            .DefineInitialDeployment(
                "grouped",
                resources =>
                {
                    resources.AddNetwork("app");
                },
                environmentId: "local",
                metadata: new Dictionary<string, string>
                {
                    ["source"] = "test"
                });
        using var serviceProvider = services.BuildServiceProvider();

        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var resource = Assert.Single(snapshot.Resources);

        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, resource.TypeId);
        Assert.Equal("cloudshell.network:app", resource.EffectiveResourceId);
    }

    [Fact]
    public void ResourceDefinitionBuilder_ProjectsIdentityAuthoringReferences()
    {
        var graph = new ResourceDefinitionGraphBuilder();
        var api = graph.AddAspNetCoreProject("api", "src/Api/Api.csproj");

        var identity = api.Identity("api-service");
        var principal = api.Principal(
            "api-service",
            displayName: "API service",
            providerId: "identity:development");

        Assert.Equal("application.aspnet-core-project:api", identity.ResourceId);
        Assert.Equal("api-service", identity.Name);
        Assert.Equal("application.aspnet-core-project:api/api-service", api.IdentityClientId("api-service"));
        Assert.Equal("application.aspnet-core-project:api", api.IdentityClientId());
        Assert.Equal(ResourcePrincipalKind.ResourceIdentity, principal.Kind);
        Assert.Equal("application.aspnet-core-project:api/identities/api-service", principal.Id);
        Assert.Equal("application.aspnet-core-project:api", principal.SourceResourceId);
        Assert.Equal("api-service", principal.SourceIdentityName);
        Assert.Equal("API service", principal.DisplayName);
        Assert.Equal("identity:development", principal.ProviderId);
    }

    [Fact]
    public void ResourceDefinitionGraphBuilder_BuildsManualNetworkDefinition()
    {
        var graph = new ResourceDefinitionGraphBuilder();

        graph
            .AddNetwork("app")
            .WithDisplayName("App Network")
            .WithHostReadiness("hostReady")
            .WithMappingProviders("local-host", "dns");

        var deployment = graph.BuildDeployment("app-network", environmentId: "local");

        var definition = Assert.Single(deployment.Resources);
        Assert.Equal("app-network", deployment.Name);
        Assert.Equal("local", deployment.EnvironmentId);
        Assert.Equal("app", definition.Name);
        Assert.Equal("cloudshell.network:app", definition.EffectiveResourceId);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, definition.TypeId);
        Assert.Equal(NetworkResourceTypeProvider.ProviderId, definition.ProviderId);
        Assert.Equal("App Network", definition.DisplayName);
        Assert.Equal(
            "hostReady",
            definition.ResourceAttributeValues[
                NetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal(
            "local-host,dns",
            definition.ResourceAttributeValues[
                NetworkResourceTypeProvider.Attributes.MappingProviders].StringValue);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_FeedsGraphApplyPipeline()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        graph
            .AddNetwork("app")
            .WithDisplayName("App Network")
            .WithNetworkKind("Logical")
            .WithHostReadiness("logicalOnly");

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                graph.BuildDeployment("app-network", environmentId: "local"),
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 12, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();
        var state = Assert.Single(snapshot.Resources);

        Assert.Equal("cloudshell.network:app", state.EffectiveResourceId);
        Assert.Equal(NetworkResourceTypeProvider.ResourceTypeId, state.TypeId);
        Assert.Equal("App Network", state.DisplayName);
        Assert.NotNull(state.Attributes);
        Assert.Equal(
            "Logical",
            state.Attributes[NetworkResourceTypeProvider.Attributes.NetworkKind].StringValue);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsServiceDefinitionsWithDependencies()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddNetworkResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddSecretsVaultResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var network = graph
            .AddNetwork("app")
            .WithDisplayName("App Network");

        graph
            .AddConfigurationStore("settings")
            .WithDisplayName("Settings")
            .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries")
            .DependsOn(network);
        graph
            .AddSecretsVault("secrets")
            .WithDisplayName("Secrets")
            .WithEndpoint("http://localhost:5102/api/secrets/vaults/secrets/secrets")
            .DependsOn(network);

        var deployment = graph.BuildDeployment("settings-and-secrets", environmentId: "local");

        Assert.Equal(3, deployment.Resources.Count);
        var settings = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == ConfigurationStoreResourceTypeProvider.ResourceTypeId);
        var secrets = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == SecretsVaultResourceTypeProvider.ResourceTypeId);

        Assert.Equal("configuration.store:settings", settings.EffectiveResourceId);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ProviderId, settings.ProviderId);
        Assert.Equal("Settings", settings.DisplayName);
        Assert.Equal(
            "http://localhost:5101/api/configuration/stores/settings/entries",
            settings.ResourceAttributeValues[
                ConfigurationStoreResourceTypeProvider.Attributes.Endpoint].StringValue);
        var settingsDependency = Assert.Single(settings.StartupDependencies);
        Assert.True(settingsDependency.TryGetDependsOnResourceId(out var settingsDependencyId));
        Assert.Equal("cloudshell.network:app", settingsDependencyId);

        Assert.Equal("secrets.vault:secrets", secrets.EffectiveResourceId);
        Assert.Equal(SecretsVaultResourceTypeProvider.ProviderId, secrets.ProviderId);
        Assert.Equal("Secrets", secrets.DisplayName);
        Assert.Equal(
            "http://localhost:5102/api/secrets/vaults/secrets/secrets",
            secrets.ResourceAttributeValues[
                SecretsVaultResourceTypeProvider.Attributes.Endpoint].StringValue);
        var secretsDependency = Assert.Single(secrets.StartupDependencies);
        Assert.True(secretsDependency.TryGetDependsOnResourceId(out var secretsDependencyId));
        Assert.Equal("cloudshell.network:app", secretsDependencyId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 13, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
        var snapshot = await serviceProvider
            .GetRequiredService<ResourceGraphModel>()
            .GetSnapshotAsync();

        Assert.Equal(3, snapshot.Resources.Count);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsStorageAndVolumeDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var storage = graph
            .AddStorage("local")
            .UseLocalFileSystem("Data/storage/local");

        graph
            .AddCloudShellVolume("data")
            .UseStorage(storage)
            .UseLocalFileSystemVolume("data");

        var deployment = graph.BuildDeployment("storage-volume", environmentId: "local");

        Assert.Equal(2, deployment.Resources.Count);
        var storageDefinition = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == StorageResourceTypeProvider.ResourceTypeId);
        var volumeDefinition = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == CloudShellVolumeResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.storage:local", storageDefinition.EffectiveResourceId);
        Assert.Equal("Local Storage", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal("FileSystem", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Medium].StringValue);
        Assert.Equal("Data/storage/local", storageDefinition.ResourceAttributeValues[
            StorageResourceTypeProvider.Attributes.Location].StringValue);

        Assert.Equal("cloudshell.volume:data", volumeDefinition.EffectiveResourceId);
        Assert.Equal("Local Storage", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal("FileSystem", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.StorageMedium].StringValue);
        Assert.Equal("data", volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.SubPath].StringValue);
        Assert.Equal(true, volumeDefinition.ResourceAttributeValues[
            CloudShellVolumeResourceTypeProvider.Attributes.Persistent].BooleanValue);
        var dependency = Assert.Single(volumeDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(storage.EffectiveResourceId, dependencyId);
        Assert.Equal(StorageResourceTypeProvider.ResourceTypeId, dependency.TypeId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 14, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsSqlServerAndDatabaseDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddSqlServerResourceType();
        services.AddSqlDatabaseResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var storage = graph
            .AddStorage("local")
            .UseLocalFileSystem();
        var volume = graph
            .AddCloudShellVolume("sql-data")
            .UseStorage(storage)
            .UseLocalFileSystemVolume("sql-server");
        var server = graph
            .AddSqlServer("sql")
            .WithVersion("2022")
            .WithEdition("Developer")
            .AddEndpointRequest(
                "tds",
                "tcp",
                targetPort: 1433,
                host: "localhost",
                port: 14334,
                exposure: "Local")
            .MountVolume(volume, "/var/opt/mssql")
            .DeclareDatabase("appdb", "Application DB", ensureCreated: true);

        graph
            .AddSqlDatabase("appdb")
            .BelongsToServer(server)
            .EnsureCreated();

        var deployment = graph.BuildDeployment("sql-app", environmentId: "local");

        Assert.Equal(4, deployment.Resources.Count);
        var sqlServer = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == SqlServerResourceTypeProvider.ResourceTypeId);
        var sqlDatabase = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == SqlDatabaseResourceTypeProvider.ResourceTypeId);
        Assert.Equal("application.sql-server:sql", sqlServer.EffectiveResourceId);
        Assert.Equal("2022", sqlServer.ResourceAttributeValues[
            SqlServerResourceTypeProvider.Attributes.Version].StringValue);
        var endpoint = Assert.Single(sqlServer.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            SqlServerResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("tds", endpoint.Name);
        Assert.Equal(1433, endpoint.TargetPort);
        Assert.Equal(14334, endpoint.Port);
        var sqlConfiguration = sqlServer.GetConfiguration<SqlServerConfiguration>(
            SqlServerResourceTypeProvider.ConfigurationSection);
        var databaseConfiguration = Assert.Single(sqlConfiguration!.Databases);
        Assert.Equal("appdb", databaseConfiguration.Name);
        Assert.Equal("Application DB", databaseConfiguration.DisplayName);
        Assert.True(databaseConfiguration.EnsureCreated);
        var volumeConsumer = sqlServer.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var mount = Assert.Single(volumeConsumer!.Mounts);
        Assert.Equal(volume.EffectiveResourceId, mount.Volume);
        Assert.Equal("/var/opt/mssql", mount.TargetPath);

        Assert.Equal("application.sql-database:appdb", sqlDatabase.EffectiveResourceId);
        Assert.Equal("appdb", sqlDatabase.ResourceAttributeValues[
            SqlDatabaseResourceTypeProvider.Attributes.DatabaseName].StringValue);
        Assert.Equal(true, sqlDatabase.ResourceAttributeValues[
            SqlDatabaseResourceTypeProvider.Attributes.EnsureCreated].BooleanValue);
        var dependency = Assert.Single(sqlDatabase.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(server.EffectiveResourceId, dependencyId);
        Assert.Equal(SqlServerResourceTypeProvider.ResourceTypeId, dependency.TypeId);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 15, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsContainerHostAndApplicationDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var storage = graph
            .AddStorage("local")
            .UseLocalFileSystem();
        var volume = graph
            .AddCloudShellVolume("data")
            .UseStorage(storage)
            .UseLocalFileSystemVolume("api");
        var host = graph
            .AddDockerHost("engine")
            .UseLocalDocker();

        graph
            .AddContainerApplication("api")
            .UseDockerHost(host)
            .WithImage("example/api:1.0")
            .WithRegistry("registry.local")
            .WithReplicas(2)
            .AddEndpointRequest(
                "http",
                "http",
                targetPort: 8080,
                host: "localhost",
                port: 5092,
                exposure: "Local")
            .AddHealthCheck(ResourceHealthCheckDefinition.Http(
                "/health",
                endpointName: "http"))
            .MountVolume(volume, "/data");

        var deployment = graph.BuildDeployment("container-app", environmentId: "local");

        Assert.Equal(4, deployment.Resources.Count);
        var hostDefinition = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == DockerHostResourceTypeProvider.ResourceTypeId);
        var appDefinition = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == ContainerApplicationResourceTypeProvider.ResourceTypeId);
        Assert.Equal("docker.host:engine", hostDefinition.EffectiveResourceId);
        Assert.Equal("local", hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.HostKind].StringValue);
        Assert.Equal("unix:///var/run/docker.sock", hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.Endpoint].StringValue);
        Assert.Equal(true, hostDefinition.ResourceAttributeValues[
            DockerHostResourceTypeProvider.Attributes.IsDefault].BooleanValue);

        Assert.Equal("application.container-app:api", appDefinition.EffectiveResourceId);
        Assert.Equal("example/api:1.0", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerImage].StringValue);
        Assert.Equal("registry.local", appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerRegistry].StringValue);
        Assert.Equal(2, appDefinition.ResourceAttributeValues[
            ContainerApplicationResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
        var endpoint = Assert.Single(appDefinition.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            ContainerApplicationResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(8080, endpoint.TargetPort);
        Assert.Equal(5092, endpoint.Port);
        var healthChecks = appDefinition.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        var healthCheck = Assert.Single(healthChecks?.Checks ?? []);
        Assert.Equal("/health", healthCheck.Source.Http?.Path);
        var dependency = Assert.Single(appDefinition.StartupDependencies);
        Assert.True(dependency.TryGetDependsOnResourceId(out var dependencyId));
        Assert.Equal(host.EffectiveResourceId, dependencyId);
        Assert.Equal(DockerHostResourceTypeProvider.ResourceTypeId, dependency.TypeId);
        var volumeConsumer = appDefinition.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var mount = Assert.Single(volumeConsumer!.Mounts);
        Assert.Equal(volume.EffectiveResourceId, mount.Volume);
        Assert.Equal("/data", mount.TargetPath);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 16, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsExecutableAndProjectDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddStorageResourceType();
        services.AddCloudShellVolumeResourceType();
        services.AddConfigurationStoreResourceType();
        services.AddExecutableApplicationResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var storage = graph
            .AddStorage("local")
            .UseLocalFileSystem();
        var volume = graph
            .AddCloudShellVolume("app-data")
            .UseStorage(storage)
            .UseLocalFileSystemVolume("app");
        var settings = graph
            .AddConfigurationStore("settings")
            .WithEndpoint("http://localhost:5101/api/configuration/stores/settings/entries");

        graph
            .AddExecutableApplication("worker")
            .WithCommand("dotnet", "run --project src/Worker/Worker.csproj", "src/Worker")
            .MountVolume(volume, "App_Data");
        graph
            .AddAspNetCoreProject("api", "src/Api/Api.csproj")
            .WithHotReload()
            .UseLaunchSettings(false)
            .WithServiceDiscovery()
            .AddEndpointRequest(
                "http",
                "http",
                host: "localhost",
                port: 5010,
                exposure: "Local")
            .WithEnvironmentVariable(
                "CLOUDSHELL_TRACE_INGEST_ENDPOINT",
                "http://localhost:5104/api/control-plane/v1/traces/ingest")
            .WithReference(settings, ConfigurationStoreResourceTypeProvider.ResourceTypeId)
            .MountVolume(volume, "App_Data")
            .WithHttpLivenessCheck(
                "/alive",
                endpointName: "http",
                interval: TimeSpan.FromSeconds(10));

        var deployment = graph.BuildDeployment("project-app", environmentId: "local");

        Assert.Equal(5, deployment.Resources.Count);
        var executable = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId);
        var project = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == AspNetCoreProjectResourceTypeProvider.ResourceTypeId);
        Assert.Equal("application.executable:worker", executable.EffectiveResourceId);
        Assert.Equal("dotnet", executable.ResourceAttributeValues[
            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath].StringValue);
        var executableConfiguration = executable.GetConfiguration<ExecutableApplicationConfiguration>(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection);
        Assert.Equal("dotnet", executableConfiguration!.Path);
        Assert.Equal("run --project src/Worker/Worker.csproj", executableConfiguration.Arguments);
        Assert.Equal("src/Worker", executableConfiguration.WorkingDirectory);
        Assert.NotNull(executable.Capabilities);
        Assert.Contains(ResourceCommonCapabilityIds.Monitoring, executable.Capabilities.Keys);

        Assert.Equal("application.aspnet-core-project:api", project.EffectiveResourceId);
        Assert.Equal("src/Api/Api.csproj", project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.ProjectPath].StringValue);
        Assert.Equal(false, project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.UseLaunchSettings].BooleanValue);
        Assert.Equal("api", project.ResourceAttributeValues[
            AspNetCoreProjectResourceTypeProvider.Attributes.ServiceDiscoveryName].StringValue);
        var endpoint = Assert.Single(project.ResourceAttributeValues.GetObject<NetworkingEndpointRequestValue[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.EndpointRequests) ?? []);
        Assert.Equal("http", endpoint.Name);
        Assert.Equal(5010, endpoint.Port);
        var environmentVariables = project.ResourceAttributeValues
            .GetObject<Dictionary<string, AspNetCoreProjectEnvironmentVariableValue>>(
                AspNetCoreProjectResourceTypeProvider.Attributes.EnvironmentVariables) ?? [];
        Assert.True(environmentVariables.ContainsKey("CLOUDSHELL_TRACE_INGEST_ENDPOINT"));
        var reference = Assert.Single(project.ResourceAttributeValues.GetObject<ResourceReference[]>(
            AspNetCoreProjectResourceTypeProvider.Attributes.References) ?? []);
        Assert.Equal(ResourceReferenceRelationships.Reference, reference.Relationship);
        Assert.Equal(settings.EffectiveResourceId, reference.Value);
        Assert.Equal(ConfigurationStoreResourceTypeProvider.ResourceTypeId, reference.TypeId);
        var healthChecks = project.GetCapability<ResourceHealthCheckDefinitionSet>(
            ResourceHealthCheckCapabilityIds.HealthChecks);
        var healthCheck = Assert.Single(healthChecks!.Checks ?? []);
        Assert.Equal("alive", healthCheck.Name);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 17, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsIdentityProvisioningDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddIdentityProvisioningResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();

        graph
            .AddIdentityProvisioning("built-in")
            .WithIdentityProvider("Built-in Identity")
            .WithIdentityProviderId("built-in")
            .WithProviderKind("built-in");

        var deployment = graph.BuildDeployment("identity", environmentId: "local");

        var identity = Assert.Single(deployment.Resources);
        Assert.Equal("cloudshell.identity-provisioning:built-in", identity.EffectiveResourceId);
        Assert.Equal(IdentityProvisioningResourceTypeProvider.ProviderId, identity.ProviderId);
        Assert.Equal("Built-in Identity", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProvider].StringValue);
        Assert.Equal("built-in", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.IdentityProviderId].StringValue);
        Assert.Equal("built-in", identity.ResourceAttributeValues[
            IdentityProvisioningResourceTypeProvider.Attributes.ProviderKind].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 18, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsExposureDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddContainerApplicationResourceType();
        services.AddNetworkResourceType();
        services.AddServiceResourceType();
        services.AddDnsZoneResourceType();
        services.AddNameMappingResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var api = graph
            .AddContainerApplication("application-topology-api")
            .WithImage("example/application-topology-api:1.0");
        var network = graph
            .AddNetwork("application-topology-local");
        var apiService = graph
            .AddService("application-topology-api-service")
            .DependsOnTarget(api, ContainerApplicationResourceTypeProvider.ResourceTypeId)
            .DependsOnNetwork(network)
            .WithRoutingMode("logical");
        var zone = graph
            .AddDnsZone("application-topology-local")
            .WithZoneName("application-topology.cloudshell.local")
            .WithProvider("hosts-file");

        graph
            .AddNameMapping("application-topology-api-local")
            .InDnsZone(zone)
            .MapsTarget(apiService, ServiceResourceTypeProvider.ResourceTypeId)
            .WithHostName("api.application-topology.cloudshell.local")
            .WithTargetEndpointName("http")
            .WithExposure("Public");

        var deployment = graph.BuildDeployment("application-exposure", environmentId: "local");

        Assert.Equal(5, deployment.Resources.Count);
        var service = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == ServiceResourceTypeProvider.ResourceTypeId);
        var nameMapping = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == NameMappingResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.service:application-topology-api-service", service.EffectiveResourceId);
        Assert.Equal("logical", service.ResourceAttributeValues[
            ServiceResourceTypeProvider.Attributes.RoutingMode].StringValue);
        Assert.Equal(
            [api.EffectiveResourceId, network.EffectiveResourceId],
            service.StartupDependencies.Select(reference => reference.Value));
        Assert.Equal("cloudshell.nameMapping:application-topology-api-local", nameMapping.EffectiveResourceId);
        Assert.Equal("api.application-topology.cloudshell.local", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.HostName].StringValue);
        Assert.Equal("http", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.TargetEndpointName].StringValue);
        Assert.Equal("Public", nameMapping.ResourceAttributeValues[
            NameMappingResourceTypeProvider.Attributes.Exposure].StringValue);
        Assert.Equal(
            [zone.EffectiveResourceId, apiService.EffectiveResourceId],
            nameMapping.StartupDependencies.Select(reference => reference.Value));

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 19, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsDockerContainerAndLocalVolumeDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerContainerResourceType();
        services.AddLocalVolumeResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();

        graph
            .AddDockerContainer("api")
            .WithImage("example/api:1.0")
            .WithRegistry("registry.local")
            .WithReplicas(2);
        graph
            .AddLocalVolume("data")
            .WithStorageMedium("local");

        var deployment = graph.BuildDeployment("docker-container", environmentId: "local");

        Assert.Equal(2, deployment.Resources.Count);
        var container = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == DockerContainerResourceTypeProvider.ResourceTypeId);
        var volume = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == LocalVolumeResourceTypeProvider.ResourceTypeId);
        Assert.Equal("docker.container:api", container.EffectiveResourceId);
        Assert.Equal("example/api:1.0", container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerImage].StringValue);
        Assert.Equal("registry.local", container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerRegistry].StringValue);
        Assert.Equal(2, container.ResourceAttributeValues[
            DockerContainerResourceTypeProvider.Attributes.ContainerReplicas].IntegerValue);
        Assert.Equal("storage.volume:data", volume.EffectiveResourceId);
        Assert.Equal("local", volume.ResourceAttributeValues[
            LocalVolumeResourceTypeProvider.Attributes.StorageMedium].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 20, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsLoadBalancerAndHostConfigurationDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddDockerHostResourceType();
        services.AddContainerApplicationResourceType();
        services.AddLoadBalancerResourceType();
        services.AddHostConfigurationSourceResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var host = graph.AddDockerHost("engine");
        var target = graph
            .AddContainerApplication("api")
            .WithImage("example/api:1.0");

        graph
            .AddLoadBalancer("edge")
            .UseHost(host)
            .AddBackendTarget(target, ContainerApplicationResourceTypeProvider.ResourceTypeId)
            .WithProvider("traefik");
        graph
            .AddHostConfigurationSource("host-settings")
            .WithSource("host");

        var deployment = graph.BuildDeployment("infrastructure", environmentId: "local");

        Assert.Equal(4, deployment.Resources.Count);
        var loadBalancer = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == LoadBalancerResourceTypeProvider.ResourceTypeId);
        var hostConfiguration = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == HostConfigurationSourceResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.loadBalancer:edge", loadBalancer.EffectiveResourceId);
        Assert.Equal("traefik", loadBalancer.ResourceAttributeValues[
            LoadBalancerResourceTypeProvider.Attributes.Provider].StringValue);
        Assert.Equal(host.EffectiveResourceId, loadBalancer.ResourceAttributeValues[
            LoadBalancerResourceTypeProvider.Attributes.HostResourceId].StringValue);
        Assert.Equal(
            [host.EffectiveResourceId, target.EffectiveResourceId],
            loadBalancer.StartupDependencies.Select(reference => reference.Value));
        Assert.Equal("configuration.host:host-settings", hostConfiguration.EffectiveResourceId);
        Assert.Equal("host", hostConfiguration.ResourceAttributeValues[
            HostConfigurationSourceResourceTypeProvider.Attributes.Source].StringValue);

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 26, 21, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    [Fact]
    public async Task ResourceDefinitionGraphBuilder_BuildsHostNetworkingDefinitions()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph();
        services.AddLocalHostNetworkResourceType();
        services.AddVirtualNetworkResourceType();
        services.AddAspNetCoreProjectResourceType();
        services.AddResourceModelGraphServices();
        using var serviceProvider = services.BuildServiceProvider();
        var graph = new ResourceDefinitionGraphBuilder();
        var hostNetwork = graph
            .AddLocalHostNetwork("host-local")
            .WithHostReadiness("ready")
            .WithNetworkingMode("localProxy");
        var api = graph
            .AddAspNetCoreProject("api", "src/Api/Api.csproj")
            .UseLaunchSettings(false);

        graph
            .AddVirtualNetwork("app")
            .DependsOn(hostNetwork, LocalHostNetworkResourceTypeProvider.ResourceTypeId)
            .DependsOn(api, AspNetCoreProjectResourceTypeProvider.ResourceTypeId)
            .AsDefault()
            .WithHostReadiness("providerRequired")
            .WithMappingProviders(hostNetwork.EffectiveResourceId);

        var deployment = graph.BuildDeployment("host-network", environmentId: "local");

        Assert.Equal(3, deployment.Resources.Count);
        var localHost = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == LocalHostNetworkResourceTypeProvider.ResourceTypeId);
        var network = Assert.Single(deployment.Resources, resource =>
            resource.TypeId == VirtualNetworkResourceTypeProvider.ResourceTypeId);
        Assert.Equal("cloudshell.hostNetworking.local:host-local", localHost.EffectiveResourceId);
        Assert.Equal("ready", localHost.ResourceAttributeValues[
            LocalHostNetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal("localProxy", localHost.ResourceAttributeValues[
            LocalHostNetworkResourceTypeProvider.Attributes.NetworkingMode].StringValue);
        Assert.Equal("cloudshell.virtualNetwork:app", network.EffectiveResourceId);
        Assert.True(network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.IsDefault].BooleanValue);
        Assert.Equal("providerRequired", network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.HostReadiness].StringValue);
        Assert.Equal(hostNetwork.EffectiveResourceId, network.ResourceAttributeValues[
            VirtualNetworkResourceTypeProvider.Attributes.MappingProviders].StringValue);
        Assert.Equal(
            [hostNetwork.EffectiveResourceId, api.EffectiveResourceId],
            network.StartupDependencies.Select(reference => reference.Value));

        var result = await serviceProvider
            .GetRequiredService<ResourceModelGraphDefinitionApplyService>()
            .ApplyDeploymentAsync(
                deployment,
                new ResourceGraphCommitContext(
                    PrincipalId: "developer",
                    Timestamp: new DateTimeOffset(2026, 6, 27, 10, 0, 0, TimeSpan.Zero)));

        Assert.False(result.HasErrors, string.Join(" ", result.Diagnostics.Select(diagnostic => diagnostic.Message)));
        Assert.True(result.IsCommitted);
    }

    private sealed class TestResourceIdConvention(string prefix) : IResourceIdConvention
    {
        public string CreateResourceId(ResourceIdConventionContext context) =>
            $"{prefix}/{context.TypeId}/{context.Name}";
    }

    private sealed record TestConfiguration(
        string AddressPrefix,
        bool Enabled);
}
