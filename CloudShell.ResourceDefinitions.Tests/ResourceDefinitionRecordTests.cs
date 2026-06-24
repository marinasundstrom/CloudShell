using System.Text.Json;
using CloudShell.ResourceDefinitions.ReferenceProviders;

namespace CloudShell.ResourceDefinitions.Tests;

public sealed class ResourceDefinitionRecordTests
{
    [Fact]
    public void FromDefinition_StoresStringKeyedPersistenceProjection()
    {
        var definition = CreateDefinition();

        var record = ResourceDefinitionRecord.FromDefinition(definition);

        Assert.Equal(definition.EffectiveResourceId, record.ResourceId);
        Assert.Equal("application.executable", record.TypeId);
        Assert.Contains("executable.path", record.Attributes!.Keys);
        Assert.Contains("storage.volumeConsumer", record.Capabilities!.Keys);
    }

    [Fact]
    public void ToDefinition_RehydratesDomainDefinitionBeforeResolution()
    {
        var definition = CreateDefinition();
        var record = ResourceDefinitionRecord.FromDefinition(definition);

        var roundTrip = record.ToDefinition();

        Assert.Equal(definition.EffectiveResourceId, roundTrip.EffectiveResourceId);
        Assert.Equal(definition.Name, roundTrip.Name);
        Assert.Equal(definition.TypeId, roundTrip.TypeId);
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
        var record = ResourceDefinitionRecord.FromDefinition(definition);

        var definitionJson = JsonSerializer.SerializeToElement(definition);
        var recordJson = JsonSerializer.SerializeToElement(record);

        Assert.True(definitionJson.TryGetProperty("TypeId", out _));
        Assert.True(recordJson.TryGetProperty("ResourceId", out _));
        Assert.NotEqual(
            definitionJson.GetRawText(),
            recordJson.GetRawText());
    }

    private static ResourceDefinition CreateDefinition() =>
        new(
            "api",
            ExecutableApplicationResourceTypeProvider.ResourceTypeId,
            ProviderId: ExecutableApplicationResourceTypeProvider.ProviderId,
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
