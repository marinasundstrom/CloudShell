using CloudShell.Abstractions.ResourceManager;
using CloudShell.ControlPlane.ResourceManager;
using CloudShell.ControlPlane.ResourceManager.Networking;

namespace CloudShell.ControlPlane.Tests;

public sealed class LocalHostNetworkProviderTests
{
    [Fact]
    public async Task GetResources_ProjectsPortableLocalHostNetworkingResource()
    {
        await using var provisioner = new LocalHostNetworkProvisioner();
        var provider = new LocalHostNetworkProvider(provisioner);

        var resource = Assert.Single(provider.GetResources());

        Assert.Equal(LocalHostNetworkProvider.ResourceId, resource.Id);
        Assert.Equal(LocalHostNetworkProvider.ResourceType, resource.EffectiveTypeId);
        Assert.Equal(ResourceClass.Infrastructure, resource.ResourceClass);
        Assert.Equal(ResourceState.Running, resource.State);
        Assert.Equal("hostNetworking", resource.ResourceAttributes[ResourceAttributeNames.InfrastructureKind]);
        Assert.Equal("ready", resource.ResourceAttributes[ResourceAttributeNames.NetworkHostReadiness]);
        Assert.Equal("cross-platform", resource.ResourceAttributes["host.os"]);
        Assert.Equal("localProxy", resource.ResourceAttributes["networking.mode"]);
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingEndpointMapper));
        Assert.True(resource.HasCapability(ResourceCapabilityIds.NetworkingHostNetwork));
    }
}
