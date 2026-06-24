using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.DependencyInjection;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionProviderDispatcherTests
{
    [Fact]
    public async Task ValidateCapabilitiesAsync_UsesRegisteredCapabilityProvider()
    {
        var dispatcher = CreateDispatcher(
            capabilityProviders: [new VolumeConsumerCapabilityProvider()],
            operationProviders: []);
        var resolved = ResolveExecutable(
            capabilities: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["storage.volumeConsumer"] = ResourceDefinitionJson.FromValue(new
                {
                    mounts = new[]
                    {
                        new { volume = "volume:data", targetPath = "App_Data" }
                    }
                })
            });

        var result = await dispatcher.ValidateCapabilitiesAsync(
            resolved,
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public async Task ValidateCapabilitiesAsync_ReportsMissingCapabilityProvider()
    {
        var dispatcher = CreateDispatcher(
            capabilityProviders: [],
            operationProviders: []);
        var resolved = ResolveExecutable(
            capabilities: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["storage.volumeConsumer"] = ResourceDefinitionJson.EmptyObject
            });

        var result = await dispatcher.ValidateCapabilitiesAsync(
            resolved,
            new ResourceDefinitionValidationContext());

        var diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.CapabilityProviderMissing, diagnostic.Code);
        Assert.Equal("storage.volumeConsumer", diagnostic.Target);
    }

    [Fact]
    public async Task ValidateOperationsAsync_UsesMatchingOperationProviderAndResolutionLevel()
    {
        var dispatcher = CreateDispatcher(
            capabilityProviders: [],
            operationProviders: [new ExecutableStartOperationProvider()]);
        var resolved = ResolveExecutable();

        var result = await dispatcher.ValidateOperationsAsync(
            resolved,
            new ResourceDefinitionValidationContext("local", "developer"));

        Assert.Empty(result.Diagnostics);
    }

    private static ResolvedResourceDefinition ResolveExecutable(
        IReadOnlyDictionary<string, JsonElement>? capabilities = null)
    {
        var resolver = new ResourceDefinitionResolver(
            [
                new(ResourceClass.Executable)
            ],
            [
                new(
                    "application.executable",
                    ResourceClass.Executable,
                    Operations:
                    [
                        new("start")
                    ])
            ]);

        return resolver.Resolve(new(
            "api",
            "application.executable",
            Capabilities: capabilities));
    }

    private static ResourceDefinitionProviderDispatcher CreateDispatcher(
        IReadOnlyList<IResourceDefinitionCapabilityProvider> capabilityProviders,
        IReadOnlyList<IResourceOperationProvider> operationProviders)
    {
        var services = new ServiceCollection();

        foreach (var provider in capabilityProviders)
        {
            services.AddSingleton<IResourceDefinitionCapabilityProvider>(provider);
        }

        foreach (var provider in operationProviders)
        {
            services.AddSingleton<IResourceOperationProvider>(provider);
        }

        services.AddSingleton<ResourceDefinitionProviderDispatcher>();

        return services
            .BuildServiceProvider()
            .GetRequiredService<ResourceDefinitionProviderDispatcher>();
    }

    private sealed class VolumeConsumerCapabilityProvider : IResourceDefinitionCapabilityProvider
    {
        public string CapabilityId => "storage.volumeConsumer";

        public bool CanValidate(
            ResolvedResourceDefinition resource,
            ResourceCapabilityResolution capability) =>
            resource.TypeDefinition.TypeId == "application.executable";

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

            var result = hasMounts
                ? ResourceDefinitionValidationResult.Success
                : ResourceDefinitionValidationResult.FromDiagnostics(
                    [
                        ResourceDefinitionDiagnostic.Error(
                            "storage.volumeConsumer.mountsRequired",
                            "At least one volume mount is required.",
                            capability.Id)
                    ]);

            return ValueTask.FromResult(result);
        }
    }

    private sealed class ExecutableStartOperationProvider : IResourceOperationProvider
    {
        public string OperationId => "start";

        public ResourceDefinitionValueSource ResolutionLevel =>
            ResourceDefinitionValueSource.TypeDefinition;

        public bool CanHandle(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation) =>
            resource.TypeDefinition.TypeId == "application.executable" &&
            operation.IsAvailable;

        public ValueTask<ResourceDefinitionValidationResult> ValidateAsync(
            ResolvedResourceDefinition resource,
            ResourceOperationResolution operation,
            ResourceDefinitionValidationContext context,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(ResourceDefinitionValidationResult.Success);
    }
}
