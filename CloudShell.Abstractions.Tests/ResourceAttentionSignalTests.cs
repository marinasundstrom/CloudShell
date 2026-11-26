using CloudShell.Abstractions.ResourceManager;
using CloudShell.Abstractions.Logs;
using CloudShell.Hosting.Localization;
using CloudShell.Hosting.ResourceManager;
using Microsoft.Extensions.Localization;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceAttentionSignalTests
{
    private static readonly TestLocalizer Localizer = new();
    private static Resource Item(ResourceState? state) => new("test", "test", "test", "test", "local", state, [], "1", DateTimeOffset.UnixEpoch, []);

    [Theory]
    [InlineData(ResourceState.Running)]
    [InlineData(ResourceState.Stopped)]
    [InlineData(null)]
    public void OrdinaryLifecycleStateIsNotAnError(ResourceState? state) =>
        Assert.Null(ResourceAttentionSignalResolver.Resolve(Item(state), null, null, Localizer));

    [Theory]
    [InlineData(ResourceState.Degraded)]
    [InlineData(ResourceState.Unknown)]
    public void ProblemLifecycleStatesHaveWarnings(ResourceState state) =>
        Assert.Equal(ResourceSignalSeverity.Warning, ResourceAttentionSignalResolver.Resolve(Item(state), null, null, Localizer)!.Severity);

    [Fact]
    public void UnhealthyRunningResourceHasAnError()
    {
        var health = new ResourceHealthSummary("test", ResourceHealthStatus.Unhealthy, DateTimeOffset.UtcNow, []);
        Assert.Equal(ResourceSignalSeverity.Error, ResourceAttentionSignalResolver.Resolve(Item(ResourceState.Running), health, null, Localizer)!.Severity);
    }

    [Fact]
    public void HealthyProbeOnStoppedResourceWarnsOnlyWhenChecksAreConfigured()
    {
        var health = new ResourceHealthSummary("test", ResourceHealthStatus.Healthy, DateTimeOffset.UtcNow, []);
        var resource = Item(ResourceState.Stopped);
        Assert.Null(ResourceAttentionSignalResolver.Resolve(resource, health, null, Localizer));
        Assert.Equal(ResourceSignalSeverity.Warning, ResourceAttentionSignalResolver.Resolve(resource with { HealthChecks = [new("/health")] }, health, null, Localizer)!.Severity);
    }

    [Fact]
    public void ActiveFailureIsPreservedEvenWhenHealthIsHealthy()
    {
        var failure = new ResourceFailureSignal(ResourceFailureKind.Start, ResourceSignalSeverity.Error, "Start failed", "start.failed", DateTimeOffset.UtcNow);
        var health = new ResourceHealthSummary("test", ResourceHealthStatus.Healthy, DateTimeOffset.UtcNow, []);
        var signal = ResourceAttentionSignalResolver.Resolve(Item(ResourceState.Stopped), health, failure, Localizer);
        Assert.Equal(ResourceSignalSeverity.Error, signal!.Severity);
        Assert.Contains("Start failed", signal.Tooltip);
    }

    [Fact]
    public void SuccessfulCreationRetryClearsEarlierCreationFailure()
    {
        var failure = new ResourceEvent("test", ResourceEventTypes.Events.Resource.CreateFailed, "Creation failed", DateTimeOffset.UnixEpoch, Severity: ResourceSignalSeverity.Error);
        var created = new ResourceEvent("test", ResourceEventTypes.Events.Resource.Created, "Created", DateTimeOffset.UnixEpoch.AddMinutes(1));
        Assert.NotNull(ResourceFailureSignalResolver.GetActiveFailure(Item(null), [failure]));
        Assert.Null(ResourceFailureSignalResolver.GetActiveFailure(Item(null), [failure, created]));
        Assert.NotNull(ResourceFailureSignalResolver.GetActiveFailure(Item(null), [created, failure with { Timestamp = created.Timestamp.AddMinutes(1) }]));
    }

    private sealed class TestLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);
        public LocalizedString this[string name, params object[] arguments] => new(name, string.Format(name, arguments));
        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
    }
}
