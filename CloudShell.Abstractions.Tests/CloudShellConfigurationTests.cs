using CloudShell.Configuration;
using Microsoft.Extensions.Configuration;

namespace CloudShell.Abstractions.Tests;

public sealed class CloudShellConfigurationTests
{
    [Fact]
    public void GetCloudShellServiceDiscoveryEndpoint_ReturnsNamedEndpointUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:example-api:http:0"] = "http://localhost:5127",
                ["services:example-api:https:0"] = "https://localhost:7127"
            })
            .Build();

        var endpoint = configuration.GetCloudShellServiceDiscoveryEndpoint("example-api", "https");

        Assert.Equal("https://localhost:7127", endpoint);
    }

    [Fact]
    public void GetCloudShellServiceDiscoveryEndpoint_ReturnsFirstEndpointUrl()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:postgres-main:postgres:0"] = "postgres://main.internal",
                ["services:postgres-main:postgres:1"] = "postgres://replica.internal"
            })
            .Build();

        var endpoint = configuration.GetCloudShellServiceDiscoveryEndpoint("postgres-main");

        Assert.Equal("postgres://main.internal", endpoint);
    }

    [Fact]
    public void GetResourceUri_ReturnsAbsoluteEndpointUri()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["services:redis-cache:tcp:0"] = "redis://cache.internal"
            })
            .Build();

        var endpoint = configuration.GetResourceUri("redis-cache", "tcp");

        Assert.NotNull(endpoint);
        Assert.Equal("redis", endpoint.Scheme);
        Assert.Equal("cache.internal", endpoint.Host);
    }
}
