using CloudShell.ControlPlane.Providers;
using CloudShell.ResourceModel;
using ResourceAttributeNames = CloudShell.Abstractions.ResourceManager.ResourceAttributeNames;
using ResourceModelResource = CloudShell.ResourceModel.Resource;

namespace CloudShell.ControlPlane.Tests;

public sealed class SqlServerResourceManagerAttributeProviderTests
{
    [Fact]
    public void GetAttributes_ReturnsRequiredMountTargetWhenSqlServerHasNoDataVolume()
    {
        var provider = new SqlServerResourceManagerAttributeProvider();
        var resource = ResolveSqlServer();

        var attributes = provider.GetAttributes(resource);

        Assert.NotNull(attributes);
        Assert.Equal(
            SqlServerResourceDefaults.DataPath,
            attributes[ResourceAttributeNames.VolumeRequiredMountTargetPaths]);
    }

    [Fact]
    public void GetAttributes_DoesNotReturnRequiredMountTargetWhenSqlServerHasDataVolume()
    {
        var provider = new SqlServerResourceManagerAttributeProvider();
        var resource = ResolveSqlServer(
            new VolumeConsumerDefinition(
            [
                new("cloudshell.volume:sql-data", SqlServerResourceDefaults.DataPath)
            ]));

        var attributes = provider.GetAttributes(resource);

        Assert.Null(attributes);
    }

    private static ResourceModelResource ResolveSqlServer(VolumeConsumerDefinition? volumeConsumer = null)
    {
        var state = new CloudShell.ResourceModel.ResourceState(
            "sql",
            SqlServerResourceTypeProvider.ResourceTypeId,
            ProviderId: SqlServerResourceTypeProvider.ProviderId,
            Capabilities: volumeConsumer is null
                ? null
                : new Dictionary<ResourceCapabilityId, System.Text.Json.JsonElement>
                {
                    [VolumeConsumerCapabilityProvider.CapabilityIdValue] =
                        ResourceDefinitionJson.FromValue(volumeConsumer)
                });
        var typeProvider = new SqlServerResourceTypeProvider();
        var resolver = new ResourceResolver(
            [SqlServerResourceTypeProvider.ClassDefinition],
            [typeProvider.TypeDefinition]);

        return resolver.Resolve(state);
    }
}
