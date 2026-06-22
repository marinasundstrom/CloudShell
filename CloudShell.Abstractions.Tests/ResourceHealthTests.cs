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
            TimeSpan.FromSeconds(2),
            IntervalSeconds: 15);

        Assert.Equal("/healthz", check.Path);
        Assert.Equal(ResourceProbeType.Liveness, check.Type);
        Assert.Equal("http", check.EndpointName);
        Assert.Equal("alive", check.Name);
        Assert.Equal(TimeSpan.FromSeconds(2), check.Timeout);
        Assert.Equal(15, check.IntervalSeconds);
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
            "process",
            intervalSeconds: 30);

        Assert.Equal(string.Empty, check.Path);
        Assert.Equal(ResourceProbeType.Liveness, check.Type);
        Assert.Equal("process", check.Name);
        Assert.Equal(30, check.IntervalSeconds);
        Assert.Equal("provider.process", check.EffectiveSource.Kind);
        Assert.False(check.EffectiveSource.IsHttp);
        Assert.Equal("applications", check.EffectiveSource.Metadata?["provider"]);
        Assert.Null(check.HttpSource);
    }

    [Fact]
    public void ResourceHealthCheckResult_CanCarryScopedObservations()
    {
        var check = new ResourceHealthCheck(
            new ResourceProbeSource("provider.container-app"),
            ResourceProbeType.Liveness,
            "liveness");
        var result = new ResourceHealthCheckResult(
            check,
            ResourceHealthStatus.Unhealthy,
            "One replica is unhealthy.",
            null,
            Observations:
            [
                new ResourceHealthScopeObservation(
                    "replica-1",
                    ResourceHealthScopeKinds.Runtime,
                    ResourceHealthStatus.Healthy,
                    "Replica is running.",
                    DisplayName: "Replica 1",
                    ResourceId: "application:api/runtime:replica-1",
                    Attributes: new Dictionary<string, string>
                    {
                        [ResourceAttributeNames.RuntimeReplicaOrdinal] = "1"
                    }),
                new ResourceHealthScopeObservation(
                    "replica-2",
                    ResourceHealthScopeKinds.Runtime,
                    ResourceHealthStatus.Unhealthy,
                    "Replica did not respond.",
                    ResourceHealthCheckOutcome.NoResponse,
                    DisplayName: "Replica 2",
                    ResourceId: "application:api/runtime:replica-2")
            ]);

        Assert.Equal(ResourceHealthStatus.Unhealthy, result.Status);
        Assert.Equal(2, result.ScopeObservations.Count);
        Assert.Equal(ResourceHealthScopeKinds.Runtime, result.ScopeObservations[0].ScopeKind);
        Assert.Equal("application:api/runtime:replica-1", result.ScopeObservations[0].ResourceId);
        Assert.Equal("1", result.ScopeObservations[0].ObservationAttributes[ResourceAttributeNames.RuntimeReplicaOrdinal]);
        Assert.Equal(ResourceHealthCheckOutcome.NoResponse, result.ScopeObservations[1].Outcome);
    }
}
