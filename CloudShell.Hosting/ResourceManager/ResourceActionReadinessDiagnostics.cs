using CloudShell.Abstractions.ControlPlane;
using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceActionReadinessDiagnostics
{
    public static IReadOnlyList<ResourceDiagnosticView> GetDiagnostics(
        Resource resource,
        ResourceOperationCapabilities capabilities)
    {
        ArgumentNullException.ThrowIfNull(resource);
        ArgumentNullException.ThrowIfNull(capabilities);

        var action = GetReadinessAction(resource);
        if (action is null ||
            capabilities.CanExecuteAction(action.Id))
        {
            return [];
        }

        var reason = capabilities.GetActionUnavailableReason(action.Id);
        if (string.IsNullOrWhiteSpace(reason))
        {
            return [];
        }

        return
        [
            new ResourceDiagnosticView(
                ResourceSignalSeverity.Warning,
                $"{action.DisplayName} readiness",
                reason)
        ];
    }

    private static ResourceAction? GetReadinessAction(Resource resource)
    {
        var preferredKind = resource.State is ResourceState.Running or ResourceState.Starting or ResourceState.Degraded
            ? ResourceActionKind.Restart
            : ResourceActionKind.Start;

        return resource.ResourceActions.FirstOrDefault(action => action.Kind == preferredKind) ??
            resource.ResourceActions.FirstOrDefault(action =>
                action.Kind is ResourceActionKind.Start or ResourceActionKind.Restart);
    }
}
