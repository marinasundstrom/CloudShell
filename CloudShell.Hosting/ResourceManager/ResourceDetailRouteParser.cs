using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal sealed record CombinedResourceDetailRoute(
    string ResourceId,
    string RequestedView);

internal static class ResourceDetailRouteParser
{
    public static bool TrySplitCombinedRoute(
        string routeResourceId,
        out CombinedResourceDetailRoute route)
    {
        ArgumentNullException.ThrowIfNull(routeResourceId);

        route = null!;
        var separator = routeResourceId.LastIndexOf('/');
        if (separator <= 0 || separator == routeResourceId.Length - 1)
        {
            return false;
        }

        var resourceId = routeResourceId[..separator];
        var requestedView = Uri.UnescapeDataString(
            routeResourceId[(separator + 1)..].Trim());
        if (string.IsNullOrWhiteSpace(resourceId) ||
            !ResourceViewId.TryParse(requestedView, out _))
        {
            return false;
        }

        route = new CombinedResourceDetailRoute(resourceId, requestedView);
        return true;
    }
}
