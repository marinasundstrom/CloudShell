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
                    ResourceDefinitionJson.FromValue(new
                    {
                        mounts = new[]
                        {
                            new { volume = "volume:data", targetPath = "App_Data" }
                        }
                    })
            });

        var result = await pipeline.ValidateAsync(
            definition,
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.False(result.HasErrors);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            result.Resource.TypeDefinition.TypeId);
        Assert.True(result.Resource.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(result.Resource.Operations.Has(ExecutableApplicationResourceTypeProvider.Operations.Start));
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
        IReadOnlyList<IResourceDefinitionCapabilityProvider> capabilityProviders,
        IReadOnlyList<IResourceOperationProvider> operationProviders) =>
        new(
            [new(ExecutableApplicationResourceTypeProvider.ClassId)],
            [new ExecutableApplicationResourceTypeProvider()],
            capabilityProviders,
            operationProviders);

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

    private sealed class VolumeConsumerCapabilityProvider : IResourceDefinitionCapabilityProvider
    {
        public static readonly ResourceCapabilityId CapabilityIdValue = "storage.volumeConsumer";

        public ResourceCapabilityId CapabilityId => CapabilityIdValue;

        public bool CanValidate(
            ResolvedResourceDefinition resource,
            ResourceCapabilityResolution capability) =>
            resource.TypeDefinition.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            ResolvedResourceDefinition resource,
            ResourceCapabilityResolution capability,
            ResourceDefinitionValidationContext context,
            CancellationToken cancellationToken = default)
        {
            using var document = JsonDocument.Parse(capability.Payload.GetRawText());
            var hasMounts = document.RootElement.TryGetProperty("mounts", out var mounts) &&
                mounts.ValueKind == JsonValueKind.Array &&
                mounts.GetArrayLength() > 0;

            return ValueTask.FromResult(hasMounts
                ? ResourceDefinitionValidationResult.Success
                : ResourceDefinitionValidationResult.FromDiagnostics(
                    [
                        ResourceDefinitionDiagnostic.Error(
                            "storage.volumeConsumer.mountsRequired",
                            "At least one volume mount is required.",
                            capability.Id)
                    ]));
        }
    }

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public ResourceOperationId OperationId =>
            ExecutableApplicationResourceTypeProvider.Operations.Start;

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation) =>
            resource.TypeDefinition.TypeId == ExecutableApplicationResourceTypeProvider.ResourceTypeId &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation,
            ResourceDefinitionValidationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }
}
