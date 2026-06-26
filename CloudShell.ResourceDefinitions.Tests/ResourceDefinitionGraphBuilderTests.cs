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
}
