using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceModelResolverTests
{
    [Fact]
    public void Resolve_ComposesResourceFromTypeClassAndResourceOwnedState()
    {
        var resolver = CreateResolver();
        var state = CreateState("./api");

        var resource = resolver.Resolve(state);

        Assert.Empty(resource.Diagnostics);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, resource.Class.ClassId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, resource.Type.TypeId);
        Assert.Equal("executable", resource.Class.Attributes.GetString("workload.kind"));
        Assert.Equal("dotnet", resource.Type.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal("./api", resource.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.True(resource.Capabilities.Has("logs.sources"));
        Assert.True(resource.Capabilities.Has("monitoring"));
        Assert.True(resource.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.True(resource.Operations.Has("start"));
        Assert.True(resource.Operations.Has("restart"));
    }

    [Fact]
    public void ResourceDefinition_RendersFromAndAppliesToResourceState()
    {
        var resolver = CreateResolver();
        var resource = resolver.Resolve(CreateState("./api"));

        var rendered = resource.ToDefinition();
        var changed = rendered with
        {
            Attributes = new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./worker"
            }
        };

        var updated = resolver.Resolve(resource.State.ApplyDefinition(changed));

        Assert.Equal("./api", rendered.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("./worker", updated.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ResourceTypeId, updated.Type.TypeId);
        Assert.Equal(ExecutableApplicationResourceTypeProvider.ClassId, updated.Class.ClassId);
    }

    [Fact]
    public void ResourceRecord_RoundTripsResourceOwnedState()
    {
        var state = CreateState("./api");

        var record = ResourceRecord.FromState(state);
        var roundTrip = record.ToState();

        Assert.Equal(state.EffectiveResourceId, record.ResourceId);
        Assert.Equal(state.EffectiveResourceId, roundTrip.EffectiveResourceId);
        Assert.Equal("./api", roundTrip.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Contains("storage.volumeConsumer", record.Capabilities!.Keys);
    }

    private static ResourceResolver CreateResolver() =>
        new(
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

    private static ResourceState CreateState(string executablePath) =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = executablePath
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] = ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                [
                    new("volume:data", "App_Data")
                ]))
            });
}
