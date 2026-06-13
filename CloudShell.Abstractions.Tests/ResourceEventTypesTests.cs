using CloudShell.Abstractions.Logs;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceEventTypesTests
{
    [Theory]
    [InlineData(ResourceActionIds.Run, "action.lifecycle.run")]
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
        Assert.Equal("action.lifecycle.run", ResourceEventTypes.Actions.Lifecycle.Run);
        Assert.Equal("action.lifecycle.stop", ResourceEventTypes.Actions.Lifecycle.Stop);
        Assert.Equal("action.lifecycle.pause", ResourceEventTypes.Actions.Lifecycle.Pause);
        Assert.Equal("action.lifecycle.restart", ResourceEventTypes.Actions.Lifecycle.Restart);
        Assert.Equal("lifecycle.starting", ResourceEventTypes.Events.Lifecycle.Starting);
        Assert.Equal("lifecycle.started", ResourceEventTypes.Events.Lifecycle.Started);
        Assert.Equal("lifecycle.stopping", ResourceEventTypes.Events.Lifecycle.Stopping);
        Assert.Equal("lifecycle.stopped", ResourceEventTypes.Events.Lifecycle.Stopped);
    }
}
