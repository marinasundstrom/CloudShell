using System.Globalization;

namespace CloudShell.UI.Composition;

public sealed class CompositionRegistry
{
    private readonly IReadOnlyList<CompositionPageRegistration> _pages;
    private readonly IReadOnlyList<CompositionMenuRegistration> _menus;
    private readonly IReadOnlyList<CompositionSectionRegistration> _sections;

    private CompositionRegistry(
        IReadOnlyList<CompositionPageRegistration> pages,
        IReadOnlyList<CompositionMenuRegistration> menus,
        IReadOnlyList<CompositionSectionRegistration> sections)
    {
        _pages = pages;
        _menus = menus;
        _sections = sections;
    }

    public static CompositionRegistry Create(Action<CompositionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CompositionBuilder();
        configure(builder);
        return builder.Build();
    }

    public CompositionPageRegistration? GetPage(PageId pageId) =>
        _pages.FirstOrDefault(page => page.Id == pageId);

    public CompositionPageRegistration? GetPageByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _pages.FirstOrDefault(page =>
            string.Equals(page.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase));
    }

    public CompositionMenuRegistration? GetMenu(MenuId menuId) =>
        _menus.FirstOrDefault(menu => menu.Id == menuId);

    public IReadOnlyList<CompositionSectionRegistration> GetSections(
        PageId pageId,
        SectionOutletId outletId) =>
        _sections
            .Where(section => section.PageId == pageId && section.OutletId == outletId)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public string ResolveHref(CompositionTarget target) =>
        ResolveHref(target, routeParams: null);

    public string ResolveHref(
        CompositionTarget target,
        IReadOnlyDictionary<string, object?>? routeParams)
    {
        var page = _pages.FirstOrDefault(page =>
            string.Equals(page.Id.Value, target.Value, StringComparison.OrdinalIgnoreCase));
        if (page is not null)
        {
            return AppendRouteParams(page.Route, routeParams);
        }

        var section = _sections.FirstOrDefault(section =>
            string.Equals(section.Id.Value, target.Value, StringComparison.OrdinalIgnoreCase));
        if (section is not null)
        {
            var sectionPage = GetPage(section.PageId);
            if (sectionPage is not null)
            {
                return $"{AppendRouteParams(sectionPage.Route, routeParams)}#{Uri.EscapeDataString(section.Id.Value)}";
            }
        }

        return "#";
    }

    internal static void ValidateId(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Composition IDs cannot be empty.", parameterName);
        }
    }

    internal static CompositionRegistry FromBuilder(CompositionBuilder builder)
    {
        ValidateUnique(builder.Pages.Select(page => page.Id.Value), "page");
        ValidateUnique(builder.Menus.Select(menu => menu.Id.Value), "menu");
        ValidateUnique(builder.Sections.Select(section => section.Id.Value), "section");

        return new(
            builder.Pages.ToArray(),
            builder.Menus.ToArray(),
            builder.Sections.ToArray());
    }

    internal static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            return "/";
        }

        var normalized = route.StartsWith('/') ? route : "/" + route;
        var queryStart = normalized.IndexOfAny(['?', '#']);
        return queryStart >= 0 ? normalized[..queryStart] : normalized;
    }

    private static void ValidateUnique(IEnumerable<string> values, string kind)
    {
        var duplicate = values
            .GroupBy(value => value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new InvalidOperationException($"Duplicate composition {kind} ID '{duplicate.Key}'.");
        }
    }

    private static string AppendRouteParams(
        string route,
        IReadOnlyDictionary<string, object?>? routeParams)
    {
        if (routeParams is null || routeParams.Count == 0)
        {
            return route;
        }

        var query = string.Join(
            '&',
            routeParams
                .Where(parameter => parameter.Value is not null)
                .Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty)}"));

        if (string.IsNullOrEmpty(query))
        {
            return route;
        }

        return route.Contains('?')
            ? $"{route}&{query}"
            : $"{route}?{query}";
    }
}
