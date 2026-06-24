using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

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
            capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("volume:data", "App_Data")
                    ]))
            });

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
        Assert.Same(result.Resource, startOperation.Resource);
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
            capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("volume:data", "App_Data")
                    ]))
            });

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext("local", "developer"));
        var volumeConsumer = result.Resource.Capabilities.Get<VolumeConsumerCapability>();

        Assert.NotNull(volumeConsumer);

        Assert.Same(result.Resource, volumeConsumer.Resource);

        var changes = volumeConsumer.AddMount(
            new VolumeMountDefinition("volume:logs", "Logs", ReadOnly: true));
        var updatedPayload = changes.ProposedState.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        var incrementalPayload = changes.ToIncrementalDefinition().GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.True(changes.HasChanges);
        Assert.Same(result.Resource, changes.Resource);
        Assert.Single(changes.CapabilityChanges);
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
    public async Task ValidateAsync_ReturnsCombinedResolutionAndProviderDiagnostics()
    {
        var pipeline = CreatePipeline(
            capabilityProviders: [],
            operationProviders: []);
        var definition = CreateExecutableDefinition(
            path: "",
            capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] = ResourceDefinitionJson.EmptyObject
            });

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext());

        Assert.True(result.HasErrors);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == "application.executable.pathRequired");
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.CapabilityProviderMissing);
        Assert.Contains(result.Diagnostics, diagnostic =>
            diagnostic.Code == ResourceDefinitionDiagnosticCodes.OperationProviderMissing);
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
        IReadOnlyDictionary<ResourceCapabilityId, JsonElement>? capabilities = null) =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: string.IsNullOrWhiteSpace(path)
                ? null
                : new Dictionary<ResourceAttributeId, string>
                {
                    [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = path
                },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration(path, "run"))
            },
            Capabilities: capabilities);

}
