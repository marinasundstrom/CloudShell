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
        Assert.Equal(5088, endpoint.TargetPort);
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

    [Fact]
    public void ContractFactory_CreatesAddresslessEndpointContract()
    {
        var endpoint = ResourceEndpoint.Contract("http", "http", targetPort: 8080);

        Assert.Equal("http", endpoint.Name);
        Assert.Equal(string.Empty, endpoint.Address);
        Assert.Equal("http", endpoint.Protocol);
        Assert.Equal(ResourceExposureScope.Private, endpoint.Exposure);
        Assert.Equal(8080, endpoint.TargetPort);
    }

    [Fact]
    public void TryGetPort_PrefersTargetPort()
    {
        var endpoint = ResourceEndpoint.Http("http", "localhost", 5080, targetPort: 8080);

        Assert.True(endpoint.TryGetPort(out var port));
        Assert.Equal(8080, port);
    }

    [Fact]
    public void TryGetPort_FallsBackToLegacyAddressPort()
    {
        var endpoint = ResourceEndpoint.FromAddress("http", "http://localhost:5080", "http");

        Assert.True(endpoint.TryGetPort(out var port));
        Assert.Equal(5080, port);
    }

    [Fact]
    public void TryGetPort_ReturnsFalseWhenNoPortIsAvailable()
    {
        var endpoint = ResourceEndpoint.Contract("http", "http");

        Assert.False(endpoint.TryGetPort(out var port));
        Assert.Equal(0, port);
    }

    [Fact]
    public void GetEndpointNetworkAddress_PrefersProjectedMapping()
    {
        var resource = CreateResource(
            [ResourceEndpoint.Contract("http", "http", targetPort: 8080)],
            endpointNetworkMappings:
            [
                new ResourceEndpointNetworkMapping(
                    "application:api:endpoint-network-mapping:http",
                    "public-http",
                    new ResourceEndpointReference("application:api", "http"),
                    "http://localhost:5080",
                    ResourceExposureScope.Local)
            ]);

        Assert.Equal("http://localhost:5080", resource.GetEndpointNetworkAddress("http"));
    }

    [Fact]
    public void GetEndpointNetworkAddress_FallsBackToLegacyEndpointAddress()
    {
        var resource = CreateResource(
            [ResourceEndpoint.Http("http", "localhost", 5080)]);

        Assert.Equal("http://localhost:5080", resource.GetEndpointNetworkAddress("http"));
    }

    [Fact]
    public void GetResolvedEndpointAddress_PrefersMappedAddress()
    {
        var resource = CreateResource(
            [ResourceEndpoint.Http("http", "localhost", 5080)],
            endpointNetworkMappings:
            [
                ResourceEndpointNetworkMapping.ForEndpoint(
                    "application:api",
                    "http",
                    "http://localhost:6080",
                    ResourceExposureScope.Local)
            ]);

        Assert.Equal("http://localhost:6080", resource.GetResolvedEndpointAddress(" http "));
    }

    [Fact]
    public void GetResolvedEndpointAddress_FallsBackToEndpointAddress()
    {
        var resource = CreateResource(
            [ResourceEndpoint.Http("http", "localhost", 5080)],
            endpointNetworkMappings: []);

        Assert.Equal("http://localhost:5080", resource.GetResolvedEndpointAddress("http"));
    }

    [Fact]
    public void EndpointNetworkMappingFactory_NormalizesCanonicalEndpointMapping()
    {
        var mapping = ResourceEndpointNetworkMapping.ForEndpoint(
            " application:api ",
            " http ",
            " http://localhost:5080 ",
            ResourceExposureScope.Public,
            networkResourceId: " network:local ",
            providerResourceId: " networking:host-local ",
            sourceEndpointName: " ");

        Assert.Equal("application:api:endpoint-network-mapping:http", mapping.Id);
        Assert.Equal("http", mapping.Name);
        Assert.Equal(new ResourceEndpointReference("application:api", "http"), mapping.Target);
        Assert.Equal("http://localhost:5080", mapping.Address);
        Assert.Equal(ResourceExposureScope.Public, mapping.Exposure);
        Assert.Equal("network:local", mapping.NetworkResourceId);
        Assert.Equal("networking:host-local", mapping.ProviderResourceId);
        Assert.Equal("http", mapping.SourceEndpointName);
    }

    [Fact]
    public void EndpointReferenceFactory_NormalizesReferenceParts()
    {
        var reference = ResourceEndpointReference.ForEndpoint(" application:api ", " http ");

        Assert.Equal("application:api", reference.ResourceId);
        Assert.Equal("http", reference.EndpointName);
    }

    [Fact]
    public void EndpointNetworkMapping_MatchesEndpointByTargetSourceOrMappingName()
    {
        var mapping = new ResourceEndpointNetworkMapping(
            "application:api:endpoint-network-mapping:public-http",
            "public-http",
            ResourceEndpointReference.ForEndpoint("application:api", "http"),
            "http://localhost:5080",
            ResourceExposureScope.Local,
            SourceEndpointName: "frontend");

        Assert.True(mapping.MatchesEndpoint(" http "));
        Assert.True(mapping.MatchesEndpoint("FRONTEND"));
        Assert.True(mapping.MatchesEndpoint("public-http"));
        Assert.False(mapping.MatchesEndpoint("metrics"));
        Assert.False(mapping.MatchesEndpoint(" "));
    }

    private static Resource CreateResource(
        IReadOnlyList<ResourceEndpoint> endpoints,
        IReadOnlyList<ResourceEndpointNetworkMapping>? endpointNetworkMappings = null) =>
        new(
            "application:api",
            "api",
            "Application",
            "Applications",
            "local",
            ResourceState.Running,
            endpoints,
            "1.0",
            DateTimeOffset.UtcNow,
            [],
            EndpointNetworkMappings: endpointNetworkMappings);
}
