using CloudShell.Abstractions.Authorization;
using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceActionTests
{
    [Fact]
    public void Resource_ExposesEmptyActionsWhenNoneAreDefined()
    {
        var resource = CreateResource();

        Assert.Empty(resource.ResourceActions);
        Assert.Equal(ResourceClass.Generic, resource.ResourceClass);
        Assert.Empty(resource.ResourceAttributes);
    }

    [Fact]
    public void Resource_ExposesProviderDefinedActions()
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
    public void Resource_ProvidesCaseInsensitiveActionLookup()
    {
        var resource = CreateResource([ResourceAction.Stop, new ResourceAction("custom", "Custom")]);

        Assert.True(resource.HasAction(ResourceActionIds.Stop));
        Assert.True(resource.HasAction("STOP"));
        Assert.NotNull(resource.StopAction);
        Assert.Null(resource.RunAction);
        Assert.Equal("custom", resource.GetAction("CUSTOM")?.Id);
    }

    [Fact]
    public void ResourceOperationCapabilities_ProvidesCaseInsensitiveActionLookup()
    {
        var capabilities = new ResourceOperationCapabilities(
            "sample:resource",
            true,
            true,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ResourceActionIds.Run,
                ResourceActionIds.Restart
            },
            [
                new ResourceActionCapability(ResourceActionIds.Run, true),
                new ResourceActionCapability(ResourceActionIds.Stop, false, "Cannot stop while stopped."),
                new ResourceActionCapability(ResourceActionIds.Restart, true)
            ]);

        Assert.True(capabilities.CanRun);
        Assert.False(capabilities.CanStop);
        Assert.True(capabilities.CanExecuteAction("RESTART"));
        Assert.Equal("Cannot stop while stopped.", capabilities.GetActionUnavailableReason("STOP"));

        var legacyCapabilities = new ResourceOperationCapabilities(
            "sample:resource",
            true,
            true,
            new HashSet<string> { ResourceActionIds.Restart });

        Assert.True(legacyCapabilities.CanExecuteAction("RESTART"));
    }

    [Fact]
    public void StandardActions_MarkDisruptiveCommandsForConfirmation()
    {
        Assert.Equal(ResourceActionIds.Run, ResourceAction.Run.Id);
        Assert.Equal(ResourceActionIds.Stop, ResourceAction.Stop.Id);
        Assert.Equal(ResourceActionIds.Pause, ResourceAction.Pause.Id);
        Assert.Equal(ResourceActionIds.Restart, ResourceAction.Restart.Id);
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

    [Fact]
    public void ResourceActionPermissions_MapStandardActionsToLifecyclePermission()
    {
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Run));
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Stop));
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Pause));
        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Lifecycle,
            ResourceActionPermissions.GetRequiredPermission(ResourceAction.Restart));
    }

    [Fact]
    public void ResourceActionPermissions_MapCustomActionsToGenericExecutePermission()
    {
        var action = new ResourceAction("applyLoadBalancerConfiguration", "Apply");

        Assert.Equal(
            CloudShellPermissions.Resources.Actions.Execute,
            ResourceActionPermissions.GetRequiredPermission(action));
    }

    [Fact]
    public void ResourceActionPermissions_UseCustomActionPermissionWhenDeclared()
    {
        var action = new ResourceAction(
            "applyLoadBalancerConfiguration",
            "Apply",
            RequiredPermission: "CloudShell.Network/loadBalancers/apply/action");

        Assert.Equal(
            "CloudShell.Network/loadBalancers/apply/action",
            ResourceActionPermissions.GetRequiredPermission(action));
    }

    private static Resource CreateResource(IReadOnlyList<ResourceAction>? actions = null) =>
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
