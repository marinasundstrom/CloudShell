using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceEventTypesTests
{
    [Theory]
    [InlineData(ResourceActionIds.Start, "action.lifecycle.start")]
    [InlineData(ResourceActionIds.Stop, "action.lifecycle.stop")]
    [InlineData("Apply Configuration", "action.apply-configuration")]
    public void Actions_ForAction_ReturnsStableActionEventType(
        string actionId,
        string expectedEventType)
    {
        Assert.Equal(expectedEventType, ResourceEventTypes.Actions.ForAction(actionId));
        Assert.Equal($"{expectedEventType}.failed", ResourceEventTypes.Actions.ForFailedAction(actionId));
    }

    [Fact]
    public void Lifecycle_ExposesStandardLifecycleEventTypes()
    {
        Assert.Equal("action.lifecycle.start", ResourceEventTypes.Actions.Lifecycle.Start);
        Assert.Equal("action.lifecycle.stop", ResourceEventTypes.Actions.Lifecycle.Stop);
        Assert.Equal("action.lifecycle.pause", ResourceEventTypes.Actions.Lifecycle.Pause);
        Assert.Equal("action.lifecycle.restart", ResourceEventTypes.Actions.Lifecycle.Restart);
        Assert.Equal("event.lifecycle.starting", ResourceEventTypes.Events.Lifecycle.Starting);
        Assert.Equal("event.lifecycle.started", ResourceEventTypes.Events.Lifecycle.Started);
        Assert.Equal("event.lifecycle.stopping", ResourceEventTypes.Events.Lifecycle.Stopping);
        Assert.Equal("event.lifecycle.stopped", ResourceEventTypes.Events.Lifecycle.Stopped);
        Assert.Equal(
            "event.configuration.appSettings.updated",
            ResourceEventTypes.Events.Configuration.AppSettingsUpdated);
        Assert.Equal(
            "event.configuration.environmentVariables.updated",
            ResourceEventTypes.Events.Configuration.EnvironmentVariablesUpdated);
    }
}
