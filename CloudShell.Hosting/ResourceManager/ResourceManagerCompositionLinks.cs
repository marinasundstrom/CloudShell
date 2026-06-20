using CloudShell.Abstractions.ResourceManager;
using CloudShell.UI.Composition;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceManagerCompositionLinks
{
    public static string ResourceDetails(
        CompositionRegistry composition,
        string resourceId) =>
        ResourceDetails(composition, resourceId, ResourcePredefinedViewIds.Overview);

    public static string ResourceDetails(
        CompositionRegistry composition,
        string resourceId,
        ResourceViewId viewId)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var routeParams = new Dictionary<string, object?>
        {
            ["resourceId"] = resourceId
        };

        if (viewId != ResourcePredefinedViewIds.Overview)
        {
            routeParams["view"] = ResourceManagerRoutes.GetResourceViewSegment(viewId);
        }

        var href = composition.ResolveHref(
            ResourceManagerCompositionIds.ResourceDetailsPage,
            routeParams);

        return href == "#"
            ? ResourceManagerRoutes.ResourceDetails(resourceId, viewId)
            : href;
    }
}
