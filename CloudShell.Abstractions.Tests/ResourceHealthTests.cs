using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceHealthTests
{
    [Fact]
    public void ResourceHealthCheck_ExposesHttpCompatibilityFieldsAsSource()
    {
        var check = new ResourceHealthCheck(
            "/healthz",
            ResourceProbeType.Liveness,
            "http",
            "alive",
            TimeSpan.FromSeconds(2));

        Assert.Equal("/healthz", check.Path);
        Assert.Equal(ResourceProbeType.Liveness, check.Type);
        Assert.Equal("http", check.EndpointName);
        Assert.Equal("alive", check.Name);
        Assert.Equal(TimeSpan.FromSeconds(2), check.Timeout);
        Assert.True(check.EffectiveSource.IsHttp);
        Assert.Equal("/healthz", check.HttpSource?.Path);
        Assert.Equal("http", check.HttpSource?.EndpointName);
        Assert.Equal(TimeSpan.FromSeconds(2), check.HttpSource?.Timeout);
    }

    [Fact]
    public void ResourceHealthCheck_CanUseProviderOwnedProbeSource()
    {
        var check = new ResourceHealthCheck(
            new ResourceProbeSource(
                "provider.process",
                Metadata: new Dictionary<string, string>
                {
                    ["provider"] = "applications"
                }),
            ResourceProbeType.Liveness,
            "process");

        Assert.Equal(string.Empty, check.Path);
        Assert.Equal(ResourceProbeType.Liveness, check.Type);
        Assert.Equal("process", check.Name);
        Assert.Equal("provider.process", check.EffectiveSource.Kind);
        Assert.False(check.EffectiveSource.IsHttp);
        Assert.Equal("applications", check.EffectiveSource.Metadata?["provider"]);
        Assert.Null(check.HttpSource);
    }
}
