using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceActionTests
{
    [Fact]
    public void CloudResource_ExposesEmptyActionsWhenNoneAreDefined()
    {
        var resource = CreateResource();

        Assert.Empty(resource.ResourceActions);
    }

    [Fact]
    public void CloudResource_ExposesProviderDefinedActions()
    {
        var resource = CreateResource([ResourceAction.Run, new ResourceAction("custom", "Custom")]);

        Assert.Collection(
            resource.ResourceActions,
            action => Assert.Equal(ResourceActionKind.Run, action.Kind),
            action =>
            {
                Assert.Equal("custom", action.Id);
                Assert.Equal(ResourceActionKind.Custom, action.Kind);
            });
    }

    [Fact]
    public void StandardActions_MarkDisruptiveCommandsForConfirmation()
    {
        Assert.False(ResourceAction.Run.RequiresConfirmation);
        Assert.True(ResourceAction.Stop.RequiresConfirmation);
        Assert.True(ResourceAction.Pause.RequiresConfirmation);
        Assert.True(ResourceAction.Restart.RequiresConfirmation);
    }

    [Fact]
    public void StandardActions_DefinePresentationPolicySeparatelyFromActionKind()
    {
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Run.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Stop.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Inline, ResourceAction.Pause.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionDisplayStyle.Overflow, ResourceAction.Restart.EffectivePresentation.DisplayStyle);
        Assert.Equal(ResourceActionIcon.Restart, ResourceAction.Restart.EffectivePresentation.Icon);
    }

    private static CloudResource CreateResource(IReadOnlyList<ResourceAction>? actions = null) =>
        new(
            "sample:resource",
            "Sample",
            "Sample Resource",
            "Sample",
            "local",
            ResourceState.Running,
            [],
            "1.0.0",
            DateTimeOffset.UnixEpoch,
            [],
            Actions: actions);
}
