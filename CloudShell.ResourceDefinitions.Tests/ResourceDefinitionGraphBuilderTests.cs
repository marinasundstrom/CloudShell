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
}
