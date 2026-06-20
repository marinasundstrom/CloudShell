using CloudShell.Abstractions.ResourceManager;
using CloudShell.UI.Composition;
using Microsoft.AspNetCore.WebUtilities;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceManagerCompositionLinks
{
    public static string ResourceManagerPage(
        CompositionRegistry composition,
        PageId pageId,
        string fallbackHref)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentException.ThrowIfNullOrWhiteSpace(fallbackHref);

        var href = composition.ResolveHref(pageId);

        return href == "#"
            ? fallbackHref
            : href;
    }

    public static string ResourceManagerPage(
        CompositionRegistry composition,
        PageId pageId,
        string fallbackHref,
        IReadOnlyDictionary<string, string?> query)
    {
        ArgumentNullException.ThrowIfNull(query);

        var href = ResourceManagerPage(composition, pageId, fallbackHref);
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
