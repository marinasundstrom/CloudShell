using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceActionControlStateResolverTests
{
    [Fact]
    public void Resolve_EnablesAvailableAction()
    {
        var state = ResourceActionControlStateResolver.Resolve(
            ResourceAction.Start,
            CreateCapabilities(new ResourceActionCapability(ResourceActionIds.Start, true)),
            isReadOnly: false,
            isExecuting: false,
            workingLabel: "Working");

        Assert.True(state.IsEnabled);
        Assert.False(state.IsExecuting);
        Assert.Equal("Start", state.Title);
        Assert.Equal("Start", state.Label);
    }

    [Fact]
    public void Resolve_ExplainsProviderReadinessFailure()
    {
        var state = ResourceActionControlStateResolver.Resolve(
            ResourceAction.Start,
            CreateCapabilities(new ResourceActionCapability(
                ResourceActionIds.Start,
                false,
                "The container host is unavailable.")),
            isReadOnly: false,
            isExecuting: false,
            workingLabel: "Working");

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Start unavailable. The container host is unavailable.",
            state.Title);
    }

    [Fact]
    public void Resolve_ReadOnlyReasonTakesPrecedence()
    {
        var state = ResourceActionControlStateResolver.Resolve(
            ResourceAction.Stop,
            CreateCapabilities(new ResourceActionCapability(
                ResourceActionIds.Stop,
                false,
                "The resource is already stopped.")),
            isReadOnly: true,
            isExecuting: false,
            workingLabel: "Working");

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Stop unavailable. Resource Manager is in read-only mode.",
            state.Title);
    }

    [Fact]
    public void Resolve_DisablesExecutingActionAndUsesProgressLabel()
    {
        var state = ResourceActionControlStateResolver.Resolve(
            ResourceAction.Restart,
            CreateCapabilities(new ResourceActionCapability(ResourceActionIds.Restart, true)),
            isReadOnly: false,
            isExecuting: true,
            workingLabel: "Working");

        Assert.False(state.IsEnabled);
        Assert.True(state.IsExecuting);
        Assert.Equal(
            "Restart unavailable. The action is already in progress.",
            state.Title);
        Assert.Equal("Working", state.Label);
    }

    [Fact]
    public void Resolve_UiRestrictionTakesPrecedenceOverCapabilityReason()
    {
        var state = ResourceActionControlStateResolver.Resolve(
            ResourceAction.Restart,
            CreateCapabilities(new ResourceActionCapability(
                ResourceActionIds.Restart,
                false,
                "The runtime is unavailable.")),
            isReadOnly: false,
            isExecuting: false,
            workingLabel: "Working",
            uiUnavailableReason: "Resource is not user-managed.");

        Assert.False(state.IsEnabled);
        Assert.Equal(
            "Restart unavailable. Resource is not user-managed.",
            state.Title);
    }

    private static ResourceOperationCapabilities CreateCapabilities(
        ResourceActionCapability action) =>
        new(
            "application:api",
            CanManage: true,
            CanDelete: true,
            ExecutableActionIds: action.CanExecute
                ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) { action.ActionId }
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ResourceActionCapabilities: [action]);
}
