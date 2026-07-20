using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record ResourceDeleteControlState(
    bool IsEnabled,
    string Title);

internal static class ResourceDeleteControlStateResolver
{
    public static ResourceDeleteControlState Resolve(
        Resource resource,
        ResourceOperationCapabilities capabilities,
        bool isReadOnly)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(capabilities);

        if (isReadOnly)
        {
            return new(false, "Delete unavailable. Resource Manager is in read-only mode.");
        }

        if (!resource.IsNormalResource ||
            resource.Source != ResourceSource.User ||
            resource.ManagementMode != ResourceManagementMode.UserManaged)
        {
            return new(false, "Delete unavailable. Resource is not user-managed.");
        }

        return capabilities.CanDelete
            ? new(true, "Delete")
            : new(false, "Delete unavailable. The provider does not allow this resource to be deleted.");
    }
}
