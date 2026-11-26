using CloudShell.Abstractions.ResourceManager;
using CoreShell;
using Microsoft.AspNetCore.WebUtilities;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceManagerShellLinks
{
    public static string ResourceManagerPage(
        ICoreShellRouteService routes,
        CoreShellPageId pageId,
        string fallbackHref)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackHref);

        var href = ResolveHref(routes, CoreShellTarget.ForPage(pageId));

        return href == "#"
            ? fallbackHref
            : href;
    }

    public static string ResourceManagerPage(
        ICoreShellRouteService routes,
        CoreShellPageId pageId,
        string fallbackHref,
        IReadOnlyDictionary<string, string?> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var href = ResourceManagerPage(routes, pageId, fallbackHref);
        var filteredQuery = query
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .ToDictionary(
                parameter => parameter.Key,
                parameter => parameter.Value,
                StringComparer.OrdinalIgnoreCase);

        return filteredQuery.Count == 0
            ? href
            : QueryHelpers.AddQueryString(href, filteredQuery);
    }

    public static string ResourceDetails(
        ICoreShellRouteService routes,
        string resourceId) =>
        ResourceDetailsCore(routes, resourceId, null);

    public static string ResourceDetails(
        ICoreShellRouteService routes,
        string resourceId,
        ResourceViewId viewId) => ResourceDetailsCore(routes, resourceId, viewId);

    private static string ResourceDetailsCore(ICoreShellRouteService routes, string resourceId, ResourceViewId? viewId)
    {
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentException.ThrowIfNullOrWhiteSpace(resourceId);

        var routeParams = new Dictionary<string, object?>
        {
            ["resourceId"] = resourceId
        };

        if (viewId.HasValue)
        {
            routeParams["view"] = ResourceManagerRoutes.GetResourceViewSegment(viewId.Value);
        }

        var href = ResolveHref(
            routes,
            CoreShellTarget.ForPage(ResourceManagerShellIds.ResourceDetailsPage),
            routeParams);

        return href == "#"
            ? (viewId.HasValue ? ResourceManagerRoutes.ResourceDetails(resourceId, viewId.Value) : ResourceManagerRoutes.ResourceDetails(resourceId))
            : href;
    }

    private static string ResolveHref(
        ICoreShellRouteService routes,
        CoreShellTarget target,
        IReadOnlyDictionary<string, object?>? routeValues = null) =>
        routes
            .ResolveTargetAsync(target, routeValues)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult()
            .Href;
}
