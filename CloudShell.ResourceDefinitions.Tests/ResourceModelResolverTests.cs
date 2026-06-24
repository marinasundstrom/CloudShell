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
    public void ResourceDefinition_IncrementalApplyMergesResourceOwnedState()
    {
        var createdAt = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        var lastModifiedAt = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var resolver = CreateResolver();
        var state = CreateState("./api") with
        {
            Version = "7",
            CreatedAt = createdAt,
            LastModifiedAt = lastModifiedAt,
            Attributes = new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "./api",
                ["container.replicas"] = "1"
            }
        };
        var resource = resolver.Resolve(state);

        resource.SetAttribute("container.replicas", 2);
        var incremental = resource.ApplyChanges().ToIncrementalDefinition();
        var updated = resolver.Resolve(resource.State.ApplyDefinition(incremental));

        Assert.Equal("./api", updated.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal("2", updated.Attributes.GetString("container.replicas"));
        Assert.True(updated.Capabilities.Has(VolumeConsumerCapabilityProvider.CapabilityIdValue));
        Assert.Equal("7", updated.Version);
        Assert.Equal(createdAt, updated.CreatedAt);
        Assert.Equal(lastModifiedAt, updated.LastModifiedAt);
    }

    [Fact]
    public void ApplyChanges_ReturnsProposedResourceStateWithoutMutatingProjection()
    {
        var resolver = CreateResolver();
        var resource = resolver.Resolve(CreateState("./api"));

        resource.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, "./worker");

        Assert.True(resource.HasPendingChanges);
        Assert.Equal("./api", resource.ToDefinition().ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("./worker", resource.ToDefinition(includePendingChanges: true).ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);

        var changes = resource.ApplyChanges();
        var updated = resolver.Resolve(changes.ProposedState);

        Assert.True(changes.HasChanges);
        Assert.False(resource.HasPendingChanges);
        Assert.Single(changes.AttributeChanges);
        Assert.Equal("./api", resource.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal("./worker", changes.ProposedState.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("./worker", updated.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
    }

    [Fact]
    public void ChangeContext_GroupsAttributeChangesUntilApply()
    {
        var resolver = CreateResolver();
        var resource = resolver.Resolve(CreateState("./api"));

        using var changes = resource.CreateChangeContext();
        changes.SetAttribute(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, "./worker");
        changes.SetAttribute("container.replicas", 2);

        var proposed = changes.ApplyChanges();

        Assert.Equal("./api", resource.Attributes.GetString(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath));
        Assert.Equal("./worker", proposed.ProposedState.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);
        Assert.Equal("2", proposed.ProposedState.ResourceAttributes["container.replicas"]);
        Assert.Equal(2, proposed.AttributeChanges.Count);
    }

    [Fact]
    public void ResourceChangeSet_RendersIncrementalResourceDefinition()
    {
        var resolver = CreateResolver();
        var resource = resolver.Resolve(CreateState("./api"));
        using var changes = resource.CreateChangeContext();
        changes.SetAttribute("container.replicas", 2);

        var proposed = changes.ApplyChanges();
        var fullDefinition = proposed.ToDefinition();
        var incrementalDefinition = proposed.ToIncrementalDefinition();

        Assert.Contains(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, fullDefinition.ResourceAttributes.Keys);
        Assert.DoesNotContain(ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath, incrementalDefinition.ResourceAttributes.Keys);
        Assert.Equal("2", incrementalDefinition.ResourceAttributes["container.replicas"]);
        Assert.Equal(resource.Name, incrementalDefinition.Name);
        Assert.Equal(resource.Type.TypeId, incrementalDefinition.TypeId);
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
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        ["workload.kind"] = new(DefaultValue: "executable")
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
                    Attributes: new Dictionary<ResourceAttributeId, ResourceAttributeDefinition>
                    {
                        [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] =
                            new(DefaultValue: "dotnet")
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
