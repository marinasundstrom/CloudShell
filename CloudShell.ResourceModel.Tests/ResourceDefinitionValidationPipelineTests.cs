using System.Text.Json;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceDefinitionValidationPipelineTests
{
    [Fact]
    public async Task ValidateAsync_ResolvesAndRunsRegisteredProviders()
    {
        var pipeline = CreatePipeline(
            capabilityProviders: [new VolumeConsumerCapabilityProvider()],
            operationProviders: [new ExecutableStartOperationProvider()]);
        var definition = CreateExecutableDefinition(
            path: "dotnet",
            volumeConsumer: new VolumeConsumerDefinition(
            [
                new("volume:data", "App_Data")
            ]));

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            result.Resource.Type.TypeId);
        Assert.True(result.Resource.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(result.Resource.Operations.Has(ExecutableApplicationResourceTypeProvider.Operations.Start));
        var volumeCapability = result.Resource.Capabilities.Get<VolumeConsumerCapability>();
        var startOperation = result.Resource.Operations.Get<ExecutableStartOperation>();

        Assert.NotNull(volumeCapability);
        Assert.NotNull(startOperation);
        Assert.Same(result.Resource, volumeCapability.Resource);
        Assert.Same(result.Resource, volumeCapability.Context.Resource);
        Assert.Same(result.Resource, startOperation.Resource);
        Assert.Same(result.Resource, startOperation.Context.Resource);
        Assert.Equal(ResourceDefinitionValueSource.TypeDefinition, startOperation.Definition.Source);
        Assert.True(await startOperation.CanExecuteAsync());

        var projectionResolver = new ResourceProjectionResolver(
            [new ExecutableApplicationResourceProjectionProvider()]);
        var executable = await projectionResolver.GetResourceProjectionAsync<ExecutableApplicationResource>(
            result.Resource,
            new ResourceProjectionContext("local", "developer"));

        Assert.NotNull(executable);
        var volumes = await executable.GetVolumesAsync();
        var projectedStartOperation = await executable.GetStartOperationAsync();

        Assert.Equal("dotnet", executable.ExecutablePath);
        Assert.Single(volumes);
        Assert.Equal("App_Data", volumes[0].TargetPath);
        Assert.NotNull(projectedStartOperation);
        Assert.True(projectedStartOperation.IsAvailable);
        var execution = await projectedStartOperation.ExecuteAsync();
        Assert.False(execution.HasErrors);
    }

    [Fact]
    public async Task ProjectionCapability_CanUpdateDefinitionIntent()
    {
        var pipeline = CreatePipeline(
            capabilityProviders: [new VolumeConsumerCapabilityProvider()],
            operationProviders: [new ExecutableStartOperationProvider()]);
        var definition = CreateExecutableDefinition(
            path: "dotnet",
            volumeConsumer: new VolumeConsumerDefinition(
            [
                new("volume:data", "App_Data")
            ]));

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext("local", "developer"));
        var volumeConsumer = result.Resource.Capabilities.Get<VolumeConsumerCapability>();

        Assert.NotNull(volumeConsumer);

        Assert.Same(result.Resource, volumeConsumer.Resource);
        Assert.Same(result.Resource, volumeConsumer.Context.Resource);

        var changes = volumeConsumer.AddMount(
            new VolumeMountDefinition("volume:logs", "Logs", ReadOnly: true));
        var updatedPayload = changes.ProposedState.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var incrementalPayload = changes.ToIncrementalDefinition().GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.True(changes.HasChanges);
        Assert.Same(result.Resource, changes.Resource);
        Assert.Empty(changes.CapabilityChanges);
        Assert.Contains(changes.AttributeChanges, change =>
            change.AttributeId == ResourceAttributeId.Create(
                VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString()));
        Assert.Single(volumeConsumer.Mounts);
        Assert.NotNull(updatedPayload);
        Assert.NotNull(incrementalPayload);
        Assert.Equal(2, updatedPayload.Mounts.Count);
        Assert.Equal(2, incrementalPayload.Mounts.Count);
        Assert.Contains(updatedPayload.Mounts, mount =>
            mount.Volume == "volume:logs" &&
            mount.TargetPath == "Logs" &&
            mount.ReadOnly);
    }

    [Fact]
    public async Task ProjectionCapability_ReturnsChangesThatCallerCanCommitThroughGraphBoundary()
    {
        var definition = CreateExecutableDefinition(
            path: "dotnet",
            volumeConsumer: new VolumeConsumerDefinition(
            [
                new("volume:data", "App_Data")
            ]));
        var stateProvider = new InMemoryResourceStateProvider(
            [ResourceState.FromDefinition(definition)]);
        var graphModel = new ResourceGraphModel(stateProvider);
        var typeProvider = new ExecutableApplicationResourceTypeProvider();
        var resolver = new ResourceResolver(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [typeProvider.TypeDefinition]);
        var capabilityResolver = new ResourceCapabilityResolver(
            [new VolumeConsumerCapabilityProvider()]);
        var applyDispatcher = new ResourceChangeApplyDispatcher([typeProvider]);

        var snapshot = await graphModel.GetSnapshotAsync();
        var resource = resolver.Resolve(snapshot.Resources.Single());
        await capabilityResolver.BindAsync(
            resource,
            new ResourceCapabilityProjectionContext(
                "local",
                "developer",
                new ResourceProjectionExecutionContext(resource)));
        var volumeConsumer = resource.Capabilities.Get<VolumeConsumerCapability>();

        Assert.NotNull(volumeConsumer);

        var changes = volumeConsumer.AddMount(
            new VolumeMountDefinition("volume:logs", "Logs", ReadOnly: true));
        var accepted = await applyDispatcher.ApplyChangesAsync(
            changes,
            new ResourceChangeApplyContext("local", "developer", Commit: true));

        var tracker = new ResourceGraphChangeTracker(snapshot);
        tracker.Track(accepted);
        var commit = await graphModel.CommitAsync(
            tracker.GetChanges(),
            new ResourceGraphCommitContext("local", "developer"));

        Assert.True(commit.IsCommitted);
        Assert.Equal(new ResourceGraphVersion(1), commit.Version);
        Assert.Equal(1, commit.Summary.AttributeChangeCount);
        Assert.Equal(0, commit.Summary.CapabilityChangeCount);
    }

    [Fact]
    public async Task ValidateAsync_ReturnsCombinedResolutionAndProviderDiagnostics()
    {
        var pipeline = CreatePipeline(
            capabilityProviders: [],
            operationProviders: []);
        var definition = CreateExecutableDefinition(
            path: "",
            volumeConsumer: new VolumeConsumerDefinition([]));

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "application.executable.pathRequired");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.OperationProviderMissing);
    }

    [Fact]
    public async Task ValidateAsync_UsesLastRegisteredClassDefinitionForDuplicateClassId()
    {
        var startProvider = new ExecutableStartOperationProvider();
        var pipeline = new ResourceDefinitionValidationPipeline(
            [
                new ResourceClassDefinition(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    RequiredAttributes:
                    [
                        new("class.required", "Class required attribute is missing.")
                    ]),
                ExecutableApplicationResourceTypeProvider.ClassDefinition
            ],
            [new ExecutableApplicationResourceTypeProvider()],
            operationProviders: [startProvider],
            operationProjectors: [startProvider]);

        var result = await pipeline.ValidateAsync(
            CreateExecutableDefinition("dotnet"),
            new ResourceDefinitionValidationContext());

        Assert.False(result.HasErrors);
        Assert.DoesNotContain(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing &&
            diagnostic.Target == "class.required");
    }

    private static ResourceDefinitionValidationPipeline CreatePipeline(
        IReadOnlyList<IResourceCapabilityProvider> capabilityProviders,
        IReadOnlyList<IResourceOperationProvider> operationProviders) =>
        new(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders,
            operationProviders,
            capabilityProjectors: capabilityProviders.OfType<IResourceCapabilityProjector>(),
            operationProjectors: operationProviders.OfType<IResourceOperationProjector>());

    private static ResourceDefinition CreateExecutableDefinition(
        string path,
        VolumeConsumerDefinition? volumeConsumer = null)
    {
        var attributes = new Dictionary<ResourceAttributeId, ResourceAttributeValue>
        {
            [ExecutableApplicationResourceTypeProvider.Attributes.Command] =
                ResourceAttributeValue.FromObject(new ExecutableApplicationConfiguration(path, "run"))
        };

        if (!string.IsNullOrWhiteSpace(path))
        {
            attributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = path;
        }

        if (volumeConsumer is not null)
        {
            attributes[ResourceAttributeId.Create(VolumeConsumerCapabilityProvider.CapabilityIdValue.ToString())] =
                ResourceAttributeValue.FromObject(volumeConsumer);
        }

        return
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: attributes);
    }

}
