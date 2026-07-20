using CloudShell.Abstractions.ControlPlane;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceApplyControlStateResolverTests
{
    [Fact]
    public void Resolve_EnablesConfiguredApplyHandler()
    {
        var state = ResourceApplyControlStateResolver.Resolve(
            CreateCapabilities(canManage: true),
            isReadOnly: false,
            hasApplyHandler: true,
            isApplying: false);

        Assert.True(state.IsEnabled);
        Assert.Equal("Apply changes", state.Title);
    }

    [Fact]
    public void Resolve_RejectsMissingApplyHandler()
    {
        var state = ResourceApplyControlStateResolver.Resolve(
            CreateCapabilities(canManage: true),
            isReadOnly: false,
            hasApplyHandler: false,
            isApplying: false);

        Assert.False(state.IsEnabled);
        Assert.Equal("Apply unavailable. This view has no changes to apply.", state.Title);
    }

    [Fact]
    public void Resolve_RejectsDuplicateApply()
    {
        var state = ResourceApplyControlStateResolver.Resolve(
            CreateCapabilities(canManage: true),
            isReadOnly: false,
            hasApplyHandler: true,
            isApplying: true);

        Assert.False(state.IsEnabled);
        Assert.Equal("Apply unavailable. Changes are already being applied.", state.Title);
    }

    [Theory]
    [InlineData(true, true, "Apply unavailable. Resource Manager is in read-only mode.")]
    [InlineData(false, false, "Apply unavailable. The provider does not allow this resource to be managed.")]
    public void Resolve_ExplainsResourceManagementRestriction(
        bool isReadOnly,
        bool canManage,
        string expectedTitle)
    {
        var state = ResourceApplyControlStateResolver.Resolve(
            CreateCapabilities(canManage),
            isReadOnly,
            hasApplyHandler: true,
            isApplying: false);

        Assert.False(state.IsEnabled);
        Assert.Equal(expectedTitle, state.Title);
    }

    private static ResourceOperationCapabilities CreateCapabilities(bool canManage) =>
        new(
            "application:api",
            CanManage: canManage,
            CanDelete: false,
            ExecutableActionIds: new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            ResourceActionCapabilities: []);
}
