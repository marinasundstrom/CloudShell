using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.ResourceDefinitions.ReferenceProviders;
using CloudShell.ResourceDefinitions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;
using ResourceManagerClass = CloudShell.Abstractions.ResourceManager.ResourceClass;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceManagerIntegrationTests
{
    [Fact]
    public void ResourceModelResourceProvider_ProjectsResolvedResourceIntoResourceManagerShape()
    {
        var resolved = CreateResolver().Resolve(CreateExecutableState());
        var provider = new ResourceModelResourceProvider(
            "resource-model",
            "Resource model",
            () => [resolved],
            new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal("application.executable:api", projected.Id);
        Assert.Equal("api", projected.Name);
        Assert.Equal("API", projected.DisplayName);
        Assert.Equal("application.executable", projected.Kind);
        Assert.Equal("application.executable", projected.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, projected.Provider);
        Assert.Equal(ResourceManagerClass.Executable, projected.ResourceClass);
        Assert.Equal(ResourceSource.User, projected.Source);
        Assert.Equal(ResourceManagementMode.UserManaged, projected.ManagementMode);
        Assert.Equal(["storage:data"], projected.DependsOn);
        Assert.Equal("dotnet", projected.ResourceAttributes["executable.path"]);
        Assert.Equal(ResourceGraphMembershipKinds.Declared, projected.ResourceGraphMembership);
        Assert.Contains(projected.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projected.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public void ResourceModelGraphResourceProvider_ResolvesSnapshotIntoResourceManagerShape()
    {
        var provider = new ResourceModelGraphResourceProvider(
            "resource-model",
            "Resource model",
            () => new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [CreateExecutableState()]),
            CreateResolver(),
            projectionOptions: new ResourceModelResourceManagerProjectionOptions(
                DefaultLastUpdated: new DateTimeOffset(2026, 6, 24, 0, 0, 0, TimeSpan.Zero)));

        var projected = Assert.Single(provider.GetResources());

        Assert.Equal("application.executable:api", projected.Id);
        Assert.Equal("api", projected.Name);
        Assert.Equal("API", projected.DisplayName);
        Assert.Equal("application.executable", projected.Kind);
        Assert.Equal(ResourceManagerClass.Executable, projected.ResourceClass);
        Assert.Equal(["storage:data"], projected.DependsOn);
        Assert.Equal("dotnet", projected.ResourceAttributes["executable.path"]);
        Assert.Contains(projected.ResourceCapabilities, capability =>
            capability.Id == VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString());
        Assert.Contains(projected.ResourceActions, action =>
            action.Id == ResourceActionIds.Start && action.Kind == ResourceActionKind.Start);
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_ResolvesBoundResourceFromGraph()
    {
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([CreateExecutableState()]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveAsync(
                "application.executable:api",
                new ResourceDefinitionResolutionContext("local", "developer"));

        Assert.False(resolution.HasErrors);
        Assert.Equal(ResourceGraphVersion.Initial, resolution.Version);
        var target = Assert.Single(resolution.Resources);
        Assert.Same(target, resolution.Target);
        Assert.Equal("application.executable:api", target.EffectiveResourceId);

        var volumes = target.Capabilities.Get<VolumeConsumerCapability>();
        var start = target.Operations.Get<ExecutableStartOperation>();

        Assert.NotNull(volumes);
        Assert.NotNull(start);
        Assert.Same(target, volumes.Resource);
        Assert.Same(target, start.Resource);
        Assert.Equal("storage:data", Assert.Single(volumes.Mounts).Volume);
        Assert.True(await start.CanExecuteAsync());
    }

    [Fact]
    public async Task ResourceModelGraphResourceResolver_CanResolveDependencyClosure()
    {
        var worker = CreateExecutableState("worker", dependsOn: []);
        var api = CreateExecutableState("api", dependsOn: [worker.EffectiveResourceId]);
        var services = new ServiceCollection();
        services.AddInMemoryResourceModelGraph([api, worker]);
        services.AddExecutableApplicationResourceType();
        services.AddResourceModelGraphServices(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)]);
        using var serviceProvider = services.BuildServiceProvider();

        var resolution = await serviceProvider
            .GetRequiredService<ResourceModelGraphResourceResolver>()
            .ResolveWithDependenciesAsync(api.EffectiveResourceId);

        Assert.False(resolution.HasErrors);
        Assert.Equal(
            [api.EffectiveResourceId, worker.EffectiveResourceId],
            resolution.Resources.Select(resource => resource.EffectiveResourceId));
    }

    private static ResourceState CreateExecutableState(
        string name = "api",
        IReadOnlyList<string>? dependsOn = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: name.ToUpperInvariant(),
            DependsOn: dependsOn ?? ["storage:data"],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("storage:data", "App_Data")
                    ]))
            });

    private static ResourceResolver CreateResolver() =>
        new(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
                    })
            ],
            [
                new ExecutableApplicationResourceTypeProvider().TypeDefinition
            ]);
}
