using CloudShell.Abstractions.ResourceManager;
using Microsoft.Extensions.Logging;

namespace CloudShell.ControlPlane.ResourceManager;

internal static class ResourceLogScope
{
    public static IDisposable? Begin(ILogger logger, Resource resource) =>
        Begin(logger, resource.Id, ResourceDisplayLabels.GetName(resource));

    public static IDisposable? Begin(ILogger logger, string resourceId) =>
        Begin(logger, resourceId, ResourceDisplayLabels.GetName(resourceId));

    private static IDisposable? Begin(ILogger logger, string resourceId, string resourceName) =>
        logger.BeginScope(new Dictionary<string, object?>
        {
            ["ResourceId"] = resourceId,
            ["ResourceName"] = resourceName
        });
}
