using System.Text.Json;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionResolverTests
{
    [Fact]
    public void Resolve_MergesClassTypeAndResourceDefinitionValues()
    {
        var resolver = new ResourceDefinitionResolver(
            [
                new(
                    ResourceClass.Executable,
                    Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
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
                    "application.executable",
                    ResourceClass.Executable,
                    DefaultProviderId: "applications.executable",
                    Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        [ResourceAttributeNames.ExecutablePath] = "dotnet"
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
            "application.executable",
            Attributes: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [ResourceAttributeNames.ExecutablePath] = "./api"
            },
            Capabilities: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["storage.volumeConsumer"] = ResourceDefinitionJson.FromValue(new
                {
                    mounts = new[]
                    {
                        new { volume = "volume:data", targetPath = "App_Data" }
                    }
                })
            });

        var resolved = resolver.Resolve(definition);

        Assert.Empty(resolved.Diagnostics);
        Assert.Equal(ResourceClass.Executable, resolved.ClassDefinition.ResourceClass);
        Assert.Equal("application.executable", resolved.TypeDefinition.TypeId);
        Assert.Equal("executable", resolved.Attributes.GetString("workload.kind"));
        Assert.Equal("./api", resolved.Attributes.GetString(ResourceAttributeNames.ExecutablePath));
        Assert.Equal(
            ResourceDefinitionValueSource.ResourceDefinition,
            resolved.Attributes.Resolve(ResourceAttributeNames.ExecutablePath)?.Source);
        Assert.True(resolved.Capabilities.Has("logs.sources"));
        Assert.True(resolved.Capabilities.Has("monitoring"));
        Assert.True(resolved.Capabilities.Has("storage.volumeConsumer"));
        Assert.True(resolved.Operations.Has("start"));
        Assert.True(resolved.Operations.Has("restart"));
    }

    [Fact]
    public void Resolve_ReportsRequiredAttributeDiagnostics()
    {
        var resolver = new ResourceDefinitionResolver(
            [
                new(
                    ResourceClass.Executable,
                    RequiredAttributes:
                    [
                        new(ResourceAttributeNames.ExecutablePath, "Executable path is required.")
                    ])
            ],
            [
                new("application.executable", ResourceClass.Executable)
            ]);
        var definition = new ResourceDefinition("api", "application.executable");

        var resolved = resolver.Resolve(definition);

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticSeverity.Error, diagnostic.Severity);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.RequiredAttributeMissing, diagnostic.Code);
        Assert.Equal(ResourceAttributeNames.ExecutablePath, diagnostic.Target);
    }

    [Fact]
    public void Resolve_BlocksOperationOverrideWhenInheritedOperationDisallowsOverride()
    {
        var resolver = new ResourceDefinitionResolver(
            [
                new(
                    ResourceClass.Executable,
                    Operations:
                    [
                        new("start", AllowOverride: false)
                    ])
            ],
            [
                new(
                    "application.executable",
                    ResourceClass.Executable,
                    Operations:
                    [
                        new("start", ResourceDefinitionJson.FromValue(new { policy = "type" }))
                    ])
            ]);

        var resolved = resolver.Resolve(new("api", "application.executable"));

        var diagnostic = Assert.Single(resolved.Diagnostics);
        Assert.Equal(ResourceDefinitionDiagnosticCodes.OperationOverrideNotAllowed, diagnostic.Code);
        Assert.Equal(ResourceDefinitionValueSource.ClassDefinition, resolved.Operations.Resolve("start").Source);
    }

    [Fact]
    public void ResourceDefinition_CanRoundTripPlainJsonPayloads()
    {
        var definition = new ResourceDefinition(
            "api",
            "application.executable",
            ProviderId: "applications.executable",
            DisplayName: "API",
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["executable"] = ResourceDefinitionJson.FromValue(new ExecutableConfiguration(
                    "dotnet",
                    "run",
                    "./src/Api"))
            },
            Capabilities: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                ["storage.volumeConsumer"] = ResourceDefinitionJson.FromValue(new VolumeConsumerCapability(
                    [new("volume:data", "App_Data", ReadOnly: false)]))
            });

        var json = JsonSerializer.Serialize(definition);
        var roundTrip = JsonSerializer.Deserialize<ResourceDefinition>(json);

        Assert.NotNull(roundTrip);
        Assert.Equal("api", roundTrip.Name);
        Assert.Equal("application.executable", roundTrip.TypeId);
        Assert.Equal("applications.executable", roundTrip.ProviderId);

        var executable = roundTrip.GetConfiguration<ExecutableConfiguration>("executable");
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

    private sealed record ExecutableConfiguration(
        string Path,
        string? Arguments,
        string? WorkingDirectory);

    private sealed record VolumeConsumerCapability(
        IReadOnlyList<VolumeMountDefinition> Mounts);

    private sealed record VolumeMountDefinition(
        string Volume,
        string TargetPath,
        bool ReadOnly);
}
