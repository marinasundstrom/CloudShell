using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record ResourceActionControlState(
    bool IsEnabled,
    bool IsExecuting,
    string Title,
    string Label);

internal static class ResourceActionControlStateResolver
{
    public static ResourceActionControlState Resolve(
        ResourceAction action,
        ResourceOperationCapabilities capabilities,
        bool isReadOnly,
        bool isExecuting,
        string workingLabel,
        string? uiUnavailableReason = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        ArgumentNullException.ThrowIfNull(capabilities);
        ArgumentException.ThrowIfNullOrWhiteSpace(workingLabel);

        var unavailableReason = capabilities.GetActionUnavailableReason(action.Id);
        var title = isReadOnly
            ? $"{action.DisplayName} unavailable. Resource Manager is in read-only mode."
            : !string.IsNullOrWhiteSpace(uiUnavailableReason)
                ? $"{action.DisplayName} unavailable. {uiUnavailableReason}"
            : isExecuting
                ? $"{action.DisplayName} unavailable. The action is already in progress."
            : !string.IsNullOrWhiteSpace(unavailableReason)
                ? $"{action.DisplayName} unavailable. {unavailableReason}"
                : action.DisplayName;

        return new ResourceActionControlState(
            !isReadOnly &&
                !isExecuting &&
                string.IsNullOrWhiteSpace(uiUnavailableReason) &&
                capabilities.CanExecuteAction(action.Id),
            isExecuting,
            title,
            isExecuting ? workingLabel : action.DisplayName);
    }
}
