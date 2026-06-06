using CloudShell.Configuration;
using Microsoft.Extensions.Configuration;

namespace CloudShell.Abstractions.Tests;

public sealed class CloudShellConfigurationTests
{
    [Fact]
    public void GetResourceEndpoint_ReturnsNamedEndpointUri()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:example-api:http:0"] = "http://localhost:5127",
                ["services:example-api:https:0"] = "https://localhost:7127"
            })
            .Build();

        var endpoint = configuration.GetResourceEndpoint("example-api", "https");

        Assert.NotNull(endpoint);
        Assert.Equal("https", endpoint.Scheme);
        Assert.Equal("localhost", endpoint.Host);
        Assert.Equal(7127, endpoint.Port);
    }

    [Fact]
    public void GetResourceEndpoint_ReturnsNullWhenEndpointIsMissing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:redis-cache:tcp:0"] = "redis://cache.internal"
            })
            .Build();

        var endpoint = configuration.GetResourceEndpoint("redis-cache", "http");

        Assert.Null(endpoint);
    }
}
