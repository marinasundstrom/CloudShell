using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceGraphResolverTests
{
    [Fact]
    public void ResolveResource_ReturnsResourceForResourceIdInGraph()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [worker]);

        var result = resolver.ResolveResource(snapshot, worker.EffectiveResourceId);

        Assert.True(result.IsResolved);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(worker.EffectiveResourceId, result.ResourceId);
        Assert.Equal(worker.EffectiveResourceId, result.Resource?.EffectiveResourceId);
    }

    [Fact]
    public void ResolveResource_ReturnsDiagnosticForMissingResourceId()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, []);

        var result = resolver.ResolveResource(snapshot, "application.executable:missing");

        Assert.False(result.IsResolved);
        Assert.True(result.HasErrors);
        Assert.Null(result.Resource);
        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceGraphResourceMissing, diagnostic.Code);
        Assert.Equal("application.executable:missing", diagnostic.Target);
    }

    [Fact]
    public void ResolveReference_ReturnsResourceForResourceIdReference()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [worker]);

        var result = resolver.ResolveReference(
            snapshot,
            ResourceReference.DependsOnResourceId(worker.EffectiveResourceId));

        Assert.True(result.IsResolved);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(worker.EffectiveResourceId, result.Resource?.EffectiveResourceId);
    }

    [Fact]
    public void ResolveReference_ReturnsResourceForBelongsToResourceIdReference()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var server = CreateExecutableState("server");
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [server]);

        var result = resolver.ResolveReference(
            snapshot,
            ResourceReference.BelongsToResourceId(
                server.EffectiveResourceId,
                ExecutableApplicationResourceTypeProvider.ResourceTypeId));

        Assert.True(result.IsResolved);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(server.EffectiveResourceId, result.Resource?.EffectiveResourceId);
        Assert.Equal(ResourceReferenceRelationships.BelongsTo, result.Reference.Relationship);
    }

    [Fact]
    public void ResolveReference_ReportsDiagnosticForExpectedResourceTypeMismatch()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [worker]);

        var result = resolver.ResolveReference(
            snapshot,
            ResourceReference.DependsOnResourceId(
                worker.EffectiveResourceId,
                typeId: SqlServerResourceTypeProvider.ResourceTypeId));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.False(result.IsResolved);
        Assert.True(result.HasErrors);
        Assert.Equal(worker.EffectiveResourceId, result.Resource?.EffectiveResourceId);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch, diagnostic.Code);
        Assert.Equal(worker.EffectiveResourceId, diagnostic.Target);
    }

    [Fact]
    public void ResolveReference_ReturnsUnresolvedResultForUnsupportedAddressingMode()
    {
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [worker]);
        var reference = new ResourceReference(
            "provider-native-worker",
            ResourceReferenceRelationships.DependsOn,
            ResourceReferenceAddressingModes.ProviderNative);

        var result = resolver.ResolveReference(snapshot, reference);

        Assert.False(result.IsResolved);
        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Same(reference, result.Reference);
        Assert.Null(result.Resource);
    }

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

        var reference = Assert.Single(result.ResolvedReferences);
        Assert.True(reference.IsResolved);
        Assert.Equal(worker.EffectiveResourceId, reference.Resource?.EffectiveResourceId);
        Assert.Equal(worker.EffectiveResourceId, reference.Reference.Value);
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

        var reference = Assert.Single(result.ResolvedReferences);
        Assert.True(reference.IsResolved);
        Assert.Equal(volume.EffectiveResourceId, reference.Reference.Value);
        Assert.Same(result.Resources[1], reference.Resource);
    }

    [Fact]
    public void ResolveResourceAndDependencies_ReportsExplicitDependencyTypeMismatch()
    {
        var resolver = new ResourceGraphResolver(
            CreateResourceResolver());
        var worker = CreateExecutableState("worker");
        var api = CreateExecutableState(
            "api",
            dependsOn: []) with
            {
                DependsOn =
                [
                    ResourceReference.DependsOnResourceId(
                        worker.EffectiveResourceId,
                        typeId: LocalVolumeResourceTypeProvider.ResourceTypeId)
                ]
            };
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, worker]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.True(result.HasErrors);
        Assert.Equal(
            [api.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.ResourceReferenceTypeMismatch, diagnostic.Code);
        Assert.Equal(worker.EffectiveResourceId, diagnostic.Target);

        var reference = Assert.Single(result.ResolvedReferences);
        Assert.False(reference.IsResolved);
        Assert.Equal(worker.EffectiveResourceId, reference.Reference.Value);
        Assert.Equal(LocalVolumeResourceTypeProvider.ResourceTypeId, reference.Reference.TypeId);
        Assert.Equal(worker.EffectiveResourceId, reference.Resource?.EffectiveResourceId);
    }

    [Fact]
    public void ResolveResourceAndDependencies_OnlyFollowsProviderDependenciesAddressedByResourceId()
    {
        var worker = CreateExecutableState("worker");
        var api = CreateExecutableState("api");
        var resolver = new ResourceGraphResolver(
            CreateResourceResolver(),
            [
                new StaticGraphDependencyProvider(
                    [
                        new(
                            "provider-native-worker",
                            ResourceReferenceRelationships.DependsOn,
                            ResourceReferenceAddressingModes.ProviderNative),
                        ResourceReference.DependsOnResourceId(worker.EffectiveResourceId)
                    ])
            ]);
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, worker]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [api.EffectiveResourceId, worker.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));
    }

    [Fact]
    public void ResolveResourceAndDependencies_OnlyFollowsDependsOnResourceIdReferences()
    {
        var server = CreateExecutableState("server");
        var api = CreateExecutableState("api", dependsOn: []) with
        {
            DependsOn =
            [
                ResourceReference.BelongsToResourceId(
                    server.EffectiveResourceId)
            ]
        };
        var resolver = new ResourceGraphResolver(CreateResourceResolver());
        var snapshot = new ResourceGraphSnapshot(ResourceGraphVersion.Initial, [api, server]);

        var result = resolver.ResolveResourceAndDependencies(snapshot, api.EffectiveResourceId);

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [api.EffectiveResourceId],
            result.Resources.Select(resource => resource.EffectiveResourceId));
        Assert.Empty(result.ResolvedReferences);
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
            DependsOn: ToReferences(dependsOn),
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

    private static IReadOnlyList<ResourceReference>? ToReferences(
        IReadOnlyList<string>? resourceIds) =>
        resourceIds?.Select(resourceId => ResourceReference.DependsOnResourceId(resourceId)).ToArray();

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

    private sealed class StaticGraphDependencyProvider(
        IReadOnlyList<ResourceReference> dependencies) : IResourceGraphDependencyProvider
    {
        public bool CanResolveDependencies(Resource resource) => resource.Name == "api";

        public IEnumerable<ResourceReference> GetDependencies(Resource resource) => dependencies;
    }
}
