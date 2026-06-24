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
            capabilityProjectors: [new VolumeConsumerCapabilityProvider()],
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

        var projectionResolver = new ResourceProjectionResolver(
            [new ExecutableApplicationResourceProjectionProvider()]);
        var executable = await projectionResolver.GetResourceProjectionAsync<ExecutableApplicationResource>(
            result.Projection,
            new ResourceProjectionContext("local", "developer"));

        Assert.NotNull(executable);
        var volumes = await executable.GetVolumesAsync();

        Assert.Equal("dotnet", executable.ExecutablePath);
        Assert.Single(volumes);
        Assert.Equal("App_Data", volumes[0].TargetPath);
    }

    [Fact]
    public async Task ProjectionCapability_CanUpdateDefinitionIntent()
    {
        var pipeline = CreatePipeline(
            capabilityProviders: [new VolumeConsumerCapabilityProvider()],
            capabilityProjectors: [new VolumeConsumerCapabilityProvider()],
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
        var volumeConsumer = await result.Projection.GetCapabilityAsync<VolumeConsumerCapability>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.NotNull(volumeConsumer);

        var updated = volumeConsumer.AddMount(
            result.Projection.Definition,
            new VolumeMountDefinition("volume:logs", "Logs", ReadOnly: true));
        var updatedPayload = updated.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);

        Assert.NotNull(updatedPayload);
        Assert.Equal(2, updatedPayload.Mounts.Count);
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
        IReadOnlyList<IResourceOperationProvider> operationProviders,
        IReadOnlyList<IResourceCapabilityProjector>? capabilityProjectors = null) =>
        new(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders,
            operationProviders,
            capabilityProjectors: capabilityProjectors);

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

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public ResourceOperationId OperationId =>
            ExecutableApplicationResourceTypeProvider.Operations.Start;

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            Resource resource,
            ResourceOperationResolution operation) =>
            resource.Type.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            Resource resource,
            ResourceOperationResolution operation,
            ResourceDefinitionValidationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }
}
