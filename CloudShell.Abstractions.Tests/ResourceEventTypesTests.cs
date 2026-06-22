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
        Assert.Equal(
            "event.deployment.image.updated",
            ResourceEventTypes.Events.Deployment.ImageUpdated);
        Assert.Equal(
            "event.deployment.replicas.updated",
            ResourceEventTypes.Events.Deployment.ReplicasUpdated);
        Assert.Equal(
            "event.deployment.applying",
            ResourceEventTypes.Events.Deployment.Applying);
        Assert.Equal(
            "event.deployment.applied",
            ResourceEventTypes.Events.Deployment.Applied);
        Assert.Equal(
            "event.deployment.failed",
            ResourceEventTypes.Events.Deployment.Failed);
    }

    [Theory]
    [InlineData("Success", ResourceSignalSeverity.Success)]
    [InlineData("Information", ResourceSignalSeverity.Info)]
    [InlineData("Info", ResourceSignalSeverity.Info)]
    [InlineData("Warning", ResourceSignalSeverity.Warning)]
    [InlineData("Error", ResourceSignalSeverity.Error)]
    [InlineData("custom", ResourceSignalSeverity.Warning)]
    public void ResourceSignalSeverityParser_FromName_MapsStableSeverityNames(
        string level,
        ResourceSignalSeverity expectedSeverity)
    {
        Assert.Equal(expectedSeverity, ResourceSignalSeverityParser.FromName(level));
    }

    [Theory]
    [InlineData(ResourceSignalSeverity.Success, "Success")]
    [InlineData(ResourceSignalSeverity.Info, "Information")]
    [InlineData(ResourceSignalSeverity.Warning, "Warning")]
    [InlineData(ResourceSignalSeverity.Error, "Error")]
    public void ResourceSignalSeverityParser_ToLevel_ReturnsCompatibleEventLevels(
        ResourceSignalSeverity severity,
        string expectedLevel)
    {
        Assert.Equal(expectedLevel, ResourceSignalSeverityParser.ToLevel(severity));
    }

    [Fact]
    public void ResourceEvent_ProjectsTypedSeverityFromCompatibleLevel()
    {
        var resourceEvent = new ResourceEvent(
            "application:test",
            ResourceEventTypes.Events.Lifecycle.StartFailed,
            "Resource failed to start.",
            DateTimeOffset.UtcNow,
            Severity: ResourceSignalSeverity.Error);

        Assert.Equal(ResourceSignalSeverity.Error, resourceEvent.Severity);
    }
}
