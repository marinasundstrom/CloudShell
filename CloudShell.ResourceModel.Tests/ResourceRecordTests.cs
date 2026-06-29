using System.Text.Json;
using CloudShell.ControlPlane.Providers;

namespace CloudShell.ResourceModel.Tests;

public sealed class ResourceRecordTests
{
    [Fact]
    public void FromDefinition_StoresStringKeyedPersistenceProjection()
    {
        var definition = CreateDefinition();

        var record = ResourceRecord.FromDefinition(definition);

        Assert.Equal(definition.EffectiveResourceId, record.ResourceId);
        Assert.Equal("application.executable", record.TypeId);
        Assert.Contains("executable.path", record.Attributes!.Keys);
        Assert.Contains("storage.volumeConsumer", record.Capabilities!.Keys);
    }

    [Fact]
    public void ToState_RehydratesResourceOwnedStateBeforeResolution()
    {
        var definition = CreateDefinition();
        var record = ResourceRecord.FromDefinition(definition);

        var roundTrip = record.ToState();

        Assert.Equal(definition.EffectiveResourceId, roundTrip.EffectiveResourceId);
        Assert.Equal(definition.Name, roundTrip.Name);
        Assert.Equal(definition.TypeId, roundTrip.TypeId);
        Assert.Equal("7", roundTrip.Version);
        Assert.Equal(new ResourceRevision(7), roundTrip.Revision);
        Assert.Equal(
            "dotnet",
            roundTrip.ResourceAttributes[ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath]);

        var volumeConsumer = roundTrip.GetCapability<VolumeConsumerDefinition>(
            VolumeConsumerCapabilityProvider.CapabilityIdValue);
        Assert.NotNull(volumeConsumer);
        var mount = Assert.Single(volumeConsumer.Mounts);
        Assert.Equal("volume:data", mount.Volume);
    }

    [Fact]
    public void ResourceDefinitionAndRecord_AreDifferentSerializedShapes()
    {
        var definition = CreateDefinition();
        var record = ResourceRecord.FromDefinition(definition);

        var definitionJson = JsonSerializer.SerializeToElement(definition);
        var recordJson = JsonSerializer.SerializeToElement(record);

        Assert.True(definitionJson.TryGetProperty("TypeId", out _));
        Assert.True(recordJson.TryGetProperty("ResourceId", out _));
        Assert.NotEqual(
            definitionJson.GetRawText(),
            recordJson.GetRawText());
    }

    [Fact]
    public void ResourceRecord_RoundTripsCommittedResourceMetadata()
    {
        var createdAt = new DateTimeOffset(2026, 6, 23, 12, 0, 0, TimeSpan.Zero);
        var lastModifiedAt = new DateTimeOffset(2026, 6, 24, 12, 0, 0, TimeSpan.Zero);
        var state = ResourceState.FromDefinition(CreateDefinition()) with
        {
            Version = "8",
            CreatedAt = createdAt,
            LastModifiedAt = lastModifiedAt
        };

        var record = ResourceRecord.FromState(state);
        var roundTrip = record.ToState();

        Assert.Equal("8", record.Version);
        Assert.Equal(createdAt, record.CreatedAt);
        Assert.Equal(lastModifiedAt, record.LastModifiedAt);
        Assert.Equal(new ResourceRevision(8), roundTrip.Revision);
        Assert.Equal(createdAt, roundTrip.CreatedAt);
        Assert.Equal(lastModifiedAt, roundTrip.LastModifiedAt);
    }

    [Fact]
    public void ResourceRecord_RoundTripsTypedResourceReferences()
    {
        var state = ResourceState.FromDefinition(new ResourceDefinition(
            "appdb",
            SqlDatabaseResourceTypeProvider.ResourceTypeId,
            DependsOn:
            [
                ResourceReference.DependsOnResourceId(
                    "application.sql-server:server",
                    typeId: SqlServerResourceTypeProvider.ResourceTypeId,
                    providerId: SqlServerResourceTypeProvider.ProviderId)
            ],
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [SqlDatabaseResourceTypeProvider.Attributes.DatabaseName] = "appdb"
            }));

        var record = ResourceRecord.FromState(state);
        var roundTrip = record.ToState();

        Assert.NotNull(record.Dependencies);
        var dependency = Assert.Single(record.Dependencies);
        Assert.Equal("application.sql-server:server", dependency.Value);
        Assert.Equal(SqlServerResourceTypeProvider.ResourceTypeId, dependency.TypeId);
        Assert.Equal(SqlServerResourceTypeProvider.ProviderId, dependency.ProviderId);

        var roundTripDependency = Assert.Single(roundTrip.StartupDependencies);
        Assert.Equal(dependency, roundTripDependency);
    }

    private static ResourceDefinition CreateDefinition() =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
            Version: "7",
            Attributes: new Dictionary<ResourceAttributeId, string>
            {
                [ExecutableApplicationResourceTypeProvider.Attributes.ExecutablePath] = "dotnet"
            },
            Configuration: new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase)
            {
                [ExecutableApplicationResourceTypeProvider.ConfigurationSection] =
                    ResourceDefinitionJson.FromValue(new ExecutableApplicationConfiguration("dotnet", "run"))
            },
            Capabilities: new Dictionary<ResourceCapabilityId, JsonElement>
            {
                [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                    ResourceDefinitionJson.FromValue(new VolumeConsumerDefinition(
                    [
                        new("volume:data", "App_Data")
                    ]))
            });
}
