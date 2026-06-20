using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourcePredefinedViewVisibilityTests
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

        Assert.True(ResourcePredefinedViewVisibility.HasEndpointsView(resource));
    }

    [Fact]
    public void HasEndpointsView_LightsUpFromNetworkingCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.NetworkingLoadBalancer)]
        };

        Assert.True(ResourcePredefinedViewVisibility.HasEndpointsView(resource));
    }

    [Fact]
    public void HasDnsView_LightsUpFromNameMappingType()
    {
        var resource = CreateResource() with { TypeId = "cloudshell.nameMapping" };

        Assert.True(ResourcePredefinedViewVisibility.HasDnsView(resource));
    }

    [Fact]
    public void HasIdentityView_LightsUpFromDefaultIdentityProvider()
    {
        var resource = CreateResource();

        Assert.True(ResourcePredefinedViewVisibility.HasIdentityView(
            resource,
            hasDefaultIdentityProvider: true));
        Assert.True(ResourcePredefinedViewVisibility.HasAccessControlView(
            resource,
            hasDefaultIdentityProvider: true));
    }

    [Fact]
    public void HasIdentityView_StaysHiddenWithoutDefaultIdentityProvider()
    {
        var resource = CreateResource() with
        {
            Identity = ResourceIdentityBinding.RequireIdentity()
        };

        Assert.False(ResourcePredefinedViewVisibility.HasIdentityView(resource));
        Assert.False(ResourcePredefinedViewVisibility.HasAccessControlView(resource));
    }

    [Fact]
    public void HasStorageVolumesView_LightsUpFromStorageProviderCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.StorageProvider)]
        };

        Assert.True(ResourcePredefinedViewVisibility.HasStorageVolumesView(resource));
    }

    [Fact]
    public void HasHealthView_LightsUpFromHealthChecks()
    {
        var resource = CreateResource() with
        {
            HealthChecks = [new ResourceHealthCheck("/health")]
        };

        Assert.True(ResourcePredefinedViewVisibility.HasHealthView(resource));
    }

    [Fact]
    public void HasHealthView_LightsUpFromResourceTypeSupport()
    {
        var resourceType = new ResourceTypeContribution(
            "test.web",
            "Test Web",
            "Test web resource.",
            "web",
            0,
            typeof(object),
            ProbeOptions: new ResourceTypeProbeOptions([new ResourceHealthCheck("/health")]));

        Assert.True(ResourcePredefinedViewVisibility.HasHealthView(CreateResource(), resourceType));
    }

    [Fact]
    public void HasEnvironmentView_LightsUpFromEnvironmentCapability()
    {
        var resource = CreateResource() with
        {
            Capabilities = [new(ResourceCapabilityIds.EnvironmentVariables)]
        };

        Assert.True(ResourcePredefinedViewVisibility.HasEnvironmentView(resource));
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
