using CloudShell.Abstractions.ResourceManager;
using CloudShell.Providers.Applications;

namespace CloudShell.Abstractions.Tests;

public sealed class ApplicationResourcePortResolverTests
{
    [Fact]
    public void ResolveLocalPort_ReturnsExplicitPort()
    {
        var resolver = new ApplicationResourcePortResolver(new ApplicationProviderOptions());
        var port = new ServicePort("http", 8080, Port: 18080, Protocol: "http");

        var resolved = resolver.ResolveLocalPort("application:api", port);

        Assert.Equal(18080, resolved);
    }

    [Fact]
    public void ResolveLocalPort_UsesStableAutoPortRange()
    {
        var options = new ApplicationProviderOptions
        {
            AutoLocalPortStart = 30000,
            AutoLocalPortEnd = 30010
        };
        var resolver = new ApplicationResourcePortResolver(options);
        var port = new ServicePort("http", 8080, Protocol: "http");

        var first = resolver.ResolveLocalPort("application:api", port);
        var second = resolver.ResolveLocalPort("application:api", port);

        Assert.Equal(first, second);
        Assert.InRange(first, 30000, 30010);
    }

    [Fact]
    public void ResolveReplicaProbeLocalPort_IncludesRevisionScopeWhenPresent()
    {
        var options = new ApplicationProviderOptions
        {
            AutoLocalPortStart = 31000,
            AutoLocalPortEnd = 31020
        };
        var resolver = new ApplicationResourcePortResolver(options);
        var port = new ServicePort("http", 8080, Protocol: "http");
        var firstRevision = new ResourceOrchestratorServiceInstance(
            "api-rev-a-replica-1",
            1,
            2,
            RuntimeRevisionId: "rev-a");
        var secondRevision = firstRevision with
        {
            Name = "api-rev-b-replica-1",
            RuntimeRevisionId = "rev-b"
        };

        var first = resolver.ResolveReplicaProbeLocalPort("application:api", port, firstRevision);
        var second = resolver.ResolveReplicaProbeLocalPort("application:api", port, secondRevision);

        Assert.InRange(first, 31000, 31020);
        Assert.InRange(second, 31000, 31020);
        Assert.NotEqual(first, second);
    }
}
