using CloudShell.Abstractions.ControlPlane;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record ResourceApplyControlState(
    bool IsEnabled,
    string Title);

internal static class ResourceApplyControlStateResolver
{
    public static ResourceApplyControlState Resolve(
        ResourceOperationCapabilities capabilities,
        bool isReadOnly,
        bool hasApplyHandler,
        bool isApplying)
    {
        ArgumentNullException.ThrowIfNull(capabilities);

        if (isReadOnly)
        {
            return new(false, "Apply unavailable. Resource Manager is in read-only mode.");
        }

        if (!capabilities.CanManage)
        {
            return new(false, "Apply unavailable. The provider does not allow this resource to be managed.");
        }

        if (!hasApplyHandler)
        {
            return new(false, "Apply unavailable. This view has no changes to apply.");
        }

        return isApplying
            ? new(false, "Apply unavailable. Changes are already being applied.")
            : new(true, "Apply changes");
    }
}
