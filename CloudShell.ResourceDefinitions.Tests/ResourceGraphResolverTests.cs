using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceGraphResolverTests
{
    [Fact]
    public void ResolveResourceAndDependencies_ReturnsTargetAndDependencyClosure()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var api = CreateExecutableState("api", dependsOn: [worker.EffectiveResourceId]);
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, worker]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(api.EffectiveResourceId, result.Target?.EffectiveResourceId);
        Assert.Equal(
            [api.EffectiveResourceId, worker.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));
    }

    [Fact]
    public void ResolveResourceAndDependencies_IncludesCapabilityProvidedDependencies()
    {
        var resolver = new ResourceGraphResolver(
            CreateResourceResolver(),
            [new VolumeConsumerGraphDependencyProvider()]);
        var volume = CreateLocalVolumeState("data");
        var api = CreateExecutableState(
            "api",
            mounts:
            [
                new(volume.EffectiveResourceId, "App_Data")
            ]);
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, volume]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [api.EffectiveResourceId, volume.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));
    }

    [Fact]
    public void ResolveResourceAndDependencies_ReturnsDiagnosticForMissingTarget()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, []);

        var result = resolver.ResolveResourceAndDependencies(snapshot, "application.executable:missing");

        Assert.True(result.HasErrors);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, diagnostic.Code);
        Assert.Equal("application.executable:missing", diagnostic.Target);
        Assert.Empty(result.Resources);
    }

    [Fact]
    public void ResolveResourceAndDependencies_ReturnsDiagnosticForMissingDependency()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var api = CreateExecutableState("api", dependsOn: ["application.executable:missing"]);
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.True(result.HasErrors);
        Assert.Equal(api.EffectiveResourceId, result.Target?.EffectiveResourceId);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, diagnostic.Code);
        Assert.Equal("application.executable:missing", diagnostic.Target);
    }

    [Fact]
    public void ResolveResourceAndDependencies_ReturnsDiagnosticForDependencyCycle()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var api = CreateExecutableState("api", dependsOn: ["application.executable:worker"]);
        var worker = CreateExecutableState("worker", dependsOn: [api.EffectiveResourceId]);
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, worker]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.True(result.HasErrors);
        Assert.Equal(
            [api.EffectiveResourceId, worker.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceDependencyCycle, diagnostic.Code);
        Assert.Equal(api.EffectiveResourceId, diagnostic.Target);
    }

    private static ResourceState CreateExecutableState(
        string name,
        IReadOnlyList<string>? dependsOn = null,
        IReadOnlyList<VolumeMountDefinition>? mounts = null) =>
        new(
            name,
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            DependsOn: dependsOn,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Capabilities: mounts is null
                ? null
                : new Dictionary<ResourceCapabilityId, JsonElement>
                {
                    [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                        ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(mounts))
                });

    private static ResourceState CreateLocalVolumeState(
        string name) =>
        new(
            name,
            LocalVolumeResourceTypeProvider.ResourceTypeId);

    private static ResourceResolver CreateResourceResolver() =>
        new(
            [
                ExecutableApplicationResourceTypeProvider.ClassDefinition,
                LocalVolumeResourceTypeProvider.ClassDefinition
            ],
            [
                new ExecutableApplicationResourceTypeProvider().TypeDefinition,
                new LocalVolumeResourceTypeProvider().TypeDefinition
            ]);
}
