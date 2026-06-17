using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceStandardViewVisibilityTests
{
    [Fact]
    public void HasEndpointsView_LightsUpFromEndpointShape()
    {
        var resource = CreateResource() with
        {
            Endpoints =
            [
                ResourceEndpoint.Http("http", "localhost", 5000)
            ]
        };

        Assert.True(ResourceStandardViewVisibility.HasEndpointsView(resource));
    }

    [Fact]
    public void HasEndpointsView_LightsUpFromNetworkingCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.NetworkingLoadBalancer)]
        };

        Assert.True(ResourceStandardViewVisibility.HasEndpointsView(resource));
    }

    [Fact]
    public void HasDnsView_LightsUpFromNameMappingType()
    {
        var resource = CreateResource() with { TypeId = "cloudshell.nameMapping" };

        Assert.True(ResourceStandardViewVisibility.HasDnsView(resource));
    }

    [Fact]
    public void HasIdentityView_LightsUpFromIdentityBinding()
    {
        var resource = CreateResource() with
        {
            Identity = ResourceIdentityBinding.RequireIdentity()
        };

        Assert.True(ResourceStandardViewVisibility.HasIdentityView(resource));
    }

    [Fact]
    public void HasIdentityView_LightsUpFromPermissionGrantContext()
    {
        Assert.True(ResourceStandardViewVisibility.HasIdentityView(
            CreateResource(),
            hasPermissionGrants: true));
    }

    [Fact]
    public void HasStorageVolumesView_LightsUpFromStorageProviderCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.StorageProvider)]
        };

        Assert.True(ResourceStandardViewVisibility.HasStorageVolumesView(resource));
    }

    [Fact]
    public void HasEnvironmentView_LightsUpFromEnvironmentCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.EnvironmentVariables)]
        };

        Assert.True(ResourceStandardViewVisibility.HasEnvironmentView(resource));
    }

    private static Resource CreateResource() =>
        new(
            "resource:api",
            "api",
            "Generic",
            "Test",
            "local",
            null,
            [],
            "1.0.0",
            DateTimeOffset.UtcNow,
            []);
}
