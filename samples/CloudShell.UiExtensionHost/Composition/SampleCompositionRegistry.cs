using Microsoft.AspNetCore.Components;

namespace CloudShell.UiExtensionHost.Composition;

public sealed class SampleCompositionRegistry
{
    private readonly IReadOnlyList<PageRegistration> _pages;
    private readonly IReadOnlyList<MenuRegistration> _menus;
    private readonly IReadOnlyList<SectionRegistration> _sections;

    private SampleCompositionRegistry(
        IReadOnlyList<PageRegistration> pages,
        IReadOnlyList<MenuRegistration> menus,
        IReadOnlyList<SectionRegistration> sections)
    {
        _pages = pages;
        _menus = menus;
        _sections = sections;
    }

    public static SampleCompositionRegistry Create(Action<SampleCompositionBuilder> configure)
    {
        var builder = new SampleCompositionBuilder();
        configure(builder);
        return builder.Build();
    }

    public PageRegistration? GetPage(PageId pageId) =>
        _pages.FirstOrDefault(page => page.Id == pageId);

    public MenuRegistration? GetMenu(MenuId menuId) =>
        _menus.FirstOrDefault(menu => menu.Id == menuId);

    public IReadOnlyList<SectionRegistration> GetSections(
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

    internal static SampleCompositionRegistry FromBuilder(SampleCompositionBuilder builder) =>
        new(
            builder.Pages.ToArray(),
            builder.Menus.ToArray(),
            builder.Sections.ToArray());

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
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(Convert.ToString(parameter.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty)}"));

        if (string.IsNullOrEmpty(query))
        {
            return route;
        }

        return route.Contains('?')
            ? $"{route}&{query}"
            : $"{route}?{query}";
    }
}

public sealed record PageRegistration(
    PageId Id,
    string Title,
    string Route);

public sealed record MenuRegistration(
    MenuId Id,
    string Title,
    IReadOnlyList<MenuItemRegistration> Items,
    IReadOnlyList<MenuSectionRegistration> Sections);

public sealed record MenuSectionRegistration(
    MenuSectionId Id,
    string Title,
    IReadOnlyList<MenuItemRegistration> Items,
    int Order);

public sealed record MenuItemRegistration(
    MenuItemId Id,
    string Title,
    CompositionTarget Target,
    int Order);

public sealed record SectionRegistration(
    SectionId Id,
    PageId PageId,
    SectionOutletId OutletId,
    string Title,
    Type ComponentType,
    int Order);

public sealed record CompositionContext(
    PageId PageId,
    string Route);
