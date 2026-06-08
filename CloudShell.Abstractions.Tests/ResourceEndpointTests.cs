using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceEndpointTests
{
    [Fact]
    public void HttpFactory_CreatesEndpointWithExplicitExposure()
    {
        var endpoint = ResourceEndpoint.Http(
            "public",
            "localhost",
            5088,
            ResourceExposureScope.Public);

        Assert.Equal("public", endpoint.Name);
        Assert.Equal("http://localhost:5088", endpoint.Address);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(ResourceExposureScope.Public, endpoint.Exposure);
        Assert.True(endpoint.IsExternal);
    }

    [Fact]
    public void LogicalFactory_DefaultsToPrivateExposure()
    {
        var endpoint = ResourceEndpoint.Logical(
            "network",
            "network://network:app",
            "network");

        Assert.Equal(ResourceExposureScope.Private, endpoint.Exposure);
        Assert.False(endpoint.IsExternal);
    }
}
