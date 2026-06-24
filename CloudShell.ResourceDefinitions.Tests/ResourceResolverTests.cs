using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceResolverTests
{
    [Fact]
    public void Resolve_MergesClassTypeAndResourceDefinitionValues()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        ["workload.kind"] = "executable"
                    },
                    Capabilities:
                    [
                        new("logs.sources")
                    ],
                    Operations:
                    [
                        new("start", ResourceDefinitionJson.FromValue(new { policy = "class-default" }))
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    DefaultProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
                    Attributes: new Dictionary<ResourceAttributeId, string>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
                    },
                    Capabilities:
                    [
                        new("monitoring")
                    ],
                    Operations:
                    [
                        new("restart")
                    ])
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api"
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityId] = ResourceDefinitionJson.FromValue(new
                {
                    mounts = new[]
                    {
                        new { volume = "volume:data", targetPath = "App_Data" }
                    }
                })
            });

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, resolved.Class.ClassId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, resolved.Type.TypeId);
        Assert.Equal("executable", resolved.Attributes.GetString("workload.kind"));
        Assert.Equal(
            "./api",
            resolved.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal(
            ResourceDefinitionValueSource.ResourceState,
            resolved.Attributes.Resolve(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath)?.Source);
        Assert.True(resolved.Capabilities.Has("logs.sources"));
        Assert.True(resolved.Capabilities.Has("monitoring"));
        Assert.True(resolved.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityId));
        Assert.True(resolved.Operations.Has("start"));
        Assert.True(resolved.Operations.Has("restart"));
    }

    [Fact]
    public void Resolve_ReportsRequiredAttributeDiagnostics()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    RequiredAttributes:
                    [
                        new(
                            ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath,
                            "Executable path is required.")
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId)
            ]);
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId);

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing, diagnostic.Code);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public void Resolve_BlocksOperationOverrideWhenInheritedOperationDisallowsOverride()
    {
        var resolver = new ResourceResolver(
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Operations:
                    [
                        new("start", AllowOverride: false)
                    ])
            ],
            [
                new(
                    ExecutableApplicationResourceTypeProvider.ResourceTypeId,
                    ExecutableApplicationResourceTypeProvider.ClassId,
                    Operations:
                    [
                        new("start", ResourceDefinitionJson.FromValue(new { policy = "type" }))
                    ])
            ]);

        var resolved = resolver.Resolve(new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId));

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed, diagnostic.Code);
        Assert.Equal(ResourceDefinitionValueSource.ClassDefinition, resolved.Operations.Resolve("start").Source);
    }

    [Fact]
    public void ResourceDefinition_CanRoundTripPlainJsonPayloads()
    {
        var definition = new ResourceDefinition(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            DisplayName: "API",
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration(
                    "dotnet",
                    "run",
                    "./src/Api"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityId] = ResourceDefinitionJson.FromValue(new VolumeConsumerCapability(
                    [new("volume:data", "App_Data", ReadOnly: false)]))
            });

        var json = JsonSerializer.Serialize(definition);
        var roundTrip = JsonSerializer.Deserialize<ResourceDefinition>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("api", roundTrip.Name);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, roundTrip.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ProviderId, roundTrip.ProviderId);

        var executable = roundTrip.GetConfiguration<ExecutableApplicationConfiguration>(
            ExecutableApplicationResourceTypeProvider.ConfigurationSection);
        Assert.NotNull(executable);
        Assert.Equal("dotnet", executable.Path);
        Assert.Equal("./src/Api", executable.WorkingDirectory);

        var volumeConsumer = roundTrip.GetCapability<VolumeConsumerCapability>("storage.volumeConsumer");
        Assert.NotNull(volumeConsumer);
        var mount = Assert.Single(volumeConsumer.Mounts);
        Assert.Equal("volume:data", mount.Volume);
        Assert.Equal("App_Data", mount.TargetPath);
        Assert.False(mount.ReadOnly);
    }

    private sealed record VolumeConsumerCapability(
        IReadOnlyList<VolumeMountDefinition> Mounts);

    private sealed record VolumeMountDefinition(
        string Volume,
        string TargetPath,
        bool ReadOnly);

    private static class VolumeConsumerCapabilityProvider
    {
        public static readonly ResourceCapabilityId CapabilityId = "storage.volumeConsumer";
    }
}
