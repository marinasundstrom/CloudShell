using Microsoft.AspNetCore.Components;

namespace CloudShell.UI.Composition.Blazor;

internal static class CompositionPageContextResolver
{
    public static CompositionContext? Resolve(
        CompositionRegistry composition,
        NavigationManager navigation,
        CompositionContext? context,
        PageId? pageId)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(navigation);

        if (pageId.HasValue)
        {
            return ResolvePage(composition, pageId.Value);
        }

        if (context is not null)
        {
            return context;
        }

        return ResolveRoute(composition, navigation);
    }

    private static CompositionContext? ResolvePage(
        CompositionRegistry composition,
        PageId pageId)
    {
        var page = composition.GetPage(pageId);
        return page is null
            ? null
            : new CompositionContext(page.Id, page.Route);
    }

    private static CompositionContext? ResolveRoute(
        CompositionRegistry composition,
        NavigationManager navigation)
    {
        var page = composition.GetPageByRoute(navigation.ToBaseRelativePath(navigation.Uri));
        return page is null
            ? null
            : new CompositionContext(page.Id, page.Route);
    }
}
