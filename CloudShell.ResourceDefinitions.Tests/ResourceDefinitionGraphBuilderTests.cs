using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionGraphBuilderTests
{
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
}
