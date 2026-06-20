using System.Globalization;

namespace CloudShell.UI.Composition;

public sealed class CompositionRegistry
{
    private readonly IReadOnlyList<CompositionModule> _modules;
    private readonly IReadOnlyList<CompositionPageRegistration> _pages;
    private readonly IReadOnlyList<CompositionMenuRegistration> _menus;
    private readonly IReadOnlyList<CompositionSectionOutletRegistration> _sectionOutlets;
    private readonly IReadOnlyList<CompositionSectionRegistration> _sections;

    private CompositionRegistry(
        IReadOnlyList<CompositionModule> modules,
        IReadOnlyList<CompositionPageRegistration> pages,
        IReadOnlyList<CompositionMenuRegistration> menus,
        IReadOnlyList<CompositionSectionOutletRegistration> sectionOutlets,
        IReadOnlyList<CompositionSectionRegistration> sections)
    {
        _modules = modules;
        _pages = pages;
        _menus = menus;
        _sectionOutlets = sectionOutlets;
        _sections = sections;
    }

    public static CompositionRegistry Create(Action<CompositionBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CompositionBuilder();
        configure(builder);
        return builder.Build();
    }

    public static CompositionRegistry FromModules(params CompositionModule[] modules) =>
        FromModules((IEnumerable<CompositionModule>)modules);

    public static CompositionRegistry FromModules(IEnumerable<CompositionModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var materializedModules = modules.ToArray();
        ValidateUnique(materializedModules.Select(module => module.Id.Value), "module");

        var pages = materializedModules.SelectMany(module => module.Pages).ToArray();
        var menus = materializedModules.SelectMany(module => module.Menus).ToArray();
        var sectionOutlets = materializedModules.SelectMany(module => module.SectionOutlets).ToArray();
        var sections = materializedModules.SelectMany(module => module.Sections).ToArray();

        ValidateUnique(pages.Select(page => page.Id.Value), "page");
        ValidateUnique(menus.Select(menu => menu.Id.Value), "menu");
        ValidateUnique(sectionOutlets.Select(outlet => outlet.Id.Value), "section outlet");
        ValidateUnique(sections.Select(section => section.Id.Value), "section");
        ValidateSectionOutletContributions(materializedModules);
        ValidateSectionContributions(materializedModules);

        return new(
            materializedModules,
            pages,
            menus,
            sectionOutlets,
            sections);
    }

    public IReadOnlyList<CompositionModule> Modules => _modules;

    public CompositionPageRegistration? GetPage(PageId pageId) =>
        _pages.FirstOrDefault(page => page.Id == pageId);

    public CompositionPageProjection? GetPageProjection(PageId pageId) =>
        _modules
            .SelectMany(module => module.Pages.Select(page => new CompositionPageProjection(module.Id, page)))
            .FirstOrDefault(projection => projection.Page.Id == pageId);

    public CompositionPageRegistration? GetPageByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _pages.FirstOrDefault(page =>
            string.Equals(page.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase));
    }

    public CompositionMenuRegistration? GetMenu(MenuId menuId) =>
        _menus.FirstOrDefault(menu => menu.Id == menuId);

    public CompositionMenuProjection? GetMenuProjection(MenuId menuId) =>
        _modules
            .SelectMany(module => module.Menus.Select(menu => new CompositionMenuProjection(module.Id, menu)))
            .FirstOrDefault(projection => projection.Menu.Id == menuId);

    public CompositionSectionOutletRegistration? GetSectionOutlet(SectionOutletId outletId) =>
        _sectionOutlets.FirstOrDefault(outlet => outlet.Id == outletId);

    public CompositionSectionOutletProjection? GetSectionOutletProjection(SectionOutletId outletId) =>
        _modules
            .SelectMany(module => module.SectionOutlets.Select(outlet => new CompositionSectionOutletProjection(module.Id, outlet)))
            .FirstOrDefault(projection => projection.Outlet.Id == outletId);

    public IReadOnlyList<CompositionSectionRegistration> GetSections(
        PageId pageId,
        SectionOutletId outletId) =>
        _sections
            .Where(section => section.PageId == pageId && section.OutletId == outletId)
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public IReadOnlyList<CompositionSectionProjection> GetSectionProjections(
        PageId pageId,
        SectionOutletId outletId) =>
        _modules
            .SelectMany(module => module.Sections.Select(section => new CompositionSectionProjection(module.Id, section)))
            .Where(projection => projection.Section.PageId == pageId && projection.Section.OutletId == outletId)
            .OrderBy(projection => projection.Section.Order)
            .ThenBy(projection => projection.Section.Title, StringComparer.OrdinalIgnoreCase)
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
        => FromModules(builder.BuildModule(CompositionModuleId.Host));

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

    private static void ValidateSectionOutletContributions(IReadOnlyList<CompositionModule> modules)
    {
        var pageProjections = modules
            .SelectMany(module => module.Pages.Select(page => new CompositionPageProjection(module.Id, page)))
            .ToDictionary(projection => projection.Page.Id);

        foreach (var module in modules)
        {
            foreach (var outlet in module.SectionOutlets)
            {
                if (!pageProjections.TryGetValue(outlet.PageId, out var pageProjection))
                {
                    throw new InvalidOperationException(
                        $"Composition section outlet '{outlet.Id}' targets unknown page '{outlet.PageId}'.");
                }

                if (module.Id != pageProjection.ModuleId && !pageProjection.Page.IsExtendable)
                {
                    throw new InvalidOperationException(
                        $"Composition page '{outlet.PageId}' is not extendable and cannot accept section outlet '{outlet.Id}' from module '{module.Id}'.");
                }
            }
        }
    }

    private static void ValidateSectionContributions(IReadOnlyList<CompositionModule> modules)
    {
        var outletProjections = modules
            .SelectMany(module => module.SectionOutlets.Select(outlet => new CompositionSectionOutletProjection(module.Id, outlet)))
            .ToDictionary(projection => projection.Outlet.Id);

        foreach (var module in modules)
        {
            foreach (var section in module.Sections)
            {
                if (!outletProjections.TryGetValue(section.OutletId, out var outletProjection))
                {
                    throw new InvalidOperationException(
                        $"Composition section '{section.Id}' targets unknown section outlet '{section.OutletId}'.");
                }

                if (section.PageId != outletProjection.Outlet.PageId)
                {
                    throw new InvalidOperationException(
                        $"Composition section '{section.Id}' targets page '{section.PageId}' but section outlet '{section.OutletId}' belongs to page '{outletProjection.Outlet.PageId}'.");
                }

                if (module.Id != outletProjection.ModuleId && !outletProjection.Outlet.IsExtendable)
                {
                    throw new InvalidOperationException(
                        $"Composition section outlet '{section.OutletId}' is not extendable and cannot accept section '{section.Id}' from module '{module.Id}'.");
                }
            }
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
