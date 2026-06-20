using System.Globalization;

namespace CloudShell.UI.Composition;

public sealed class CompositionRegistry
{
    private readonly IReadOnlyList<CompositionModule> _modules;
    private readonly IReadOnlyList<CompositionPageRegistration> _pages;
    private readonly IReadOnlyList<CompositionMenuRegistration> _menus;
    private readonly IReadOnlyList<CompositionSectionOutletRegistration> _sectionOutlets;
    private readonly IReadOnlyList<CompositionSectionRegistration> _sections;
    private readonly IReadOnlyDictionary<PageId, CompositionPageProjection> _pageProjectionsById;
    private readonly IReadOnlyDictionary<string, CompositionPageRegistration> _pagesByRoute;
    private readonly IReadOnlyDictionary<MenuId, CompositionMenuProjection> _menuProjectionsById;
    private readonly IReadOnlyDictionary<MenuItemId, CompositionMenuItemProjection> _menuItemProjectionsById;
    private readonly IReadOnlyDictionary<MenuId, IReadOnlyList<CompositionMenuItemProjection>> _menuItemProjectionsByMenu;
    private readonly IReadOnlyDictionary<SectionOutletId, CompositionSectionOutletProjection> _sectionOutletProjectionsById;
    private readonly IReadOnlyDictionary<(PageId PageId, SectionOutletId OutletId), IReadOnlyList<CompositionSectionRegistration>> _sectionsByOutlet;
    private readonly IReadOnlyDictionary<SectionId, CompositionSectionProjection> _sectionProjectionsById;
    private readonly IReadOnlyDictionary<(PageId PageId, SectionOutletId OutletId), IReadOnlyList<CompositionSectionProjection>> _sectionProjectionsByOutlet;
    private readonly IReadOnlyDictionary<string, CompositionPageRegistration> _pagesByTarget;
    private readonly IReadOnlyDictionary<string, CompositionMenuItemProjection> _menuItemsByTarget;
    private readonly IReadOnlyDictionary<string, CompositionSectionRegistration> _sectionsByTarget;

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
        _pageProjectionsById = modules
            .SelectMany(module => module.Pages.Select(page => new CompositionPageProjection(module.Id, page)))
            .ToDictionary(projection => projection.Page.Id);
        _pagesByRoute = pages.ToDictionary(page => page.Route, StringComparer.OrdinalIgnoreCase);
        _menuProjectionsById = modules
            .SelectMany(module => module.Menus.Select(menu => new CompositionMenuProjection(module.Id, menu)))
            .ToDictionary(projection => projection.Menu.Id);
        _menuItemProjectionsById = modules
            .SelectMany(GetMenuItemProjections)
            .ToDictionary(projection => projection.Item.Id);
        _menuItemProjectionsByMenu = _menuItemProjectionsById.Values
            .GroupBy(projection => projection.Menu.Id)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CompositionMenuItemProjection>)group.ToArray());
        _sectionOutletProjectionsById = modules
            .SelectMany(module => module.SectionOutlets.Select(outlet => new CompositionSectionOutletProjection(module.Id, outlet)))
            .ToDictionary(projection => projection.Outlet.Id);
        _sectionsByOutlet = sections
            .GroupBy(section => (section.PageId, section.OutletId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CompositionSectionRegistration>)group
                    .OrderBy(section => section.Order)
                    .ThenBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        _sectionProjectionsByOutlet = modules
            .SelectMany(module => module.Sections.Select(section => new CompositionSectionProjection(module.Id, section)))
            .GroupBy(projection => (projection.Section.PageId, projection.Section.OutletId))
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<CompositionSectionProjection>)group
                    .OrderBy(projection => projection.Section.Order)
                    .ThenBy(projection => projection.Section.Title, StringComparer.OrdinalIgnoreCase)
                    .ToArray());
        _sectionProjectionsById = modules
            .SelectMany(module => module.Sections.Select(section => new CompositionSectionProjection(module.Id, section)))
            .ToDictionary(projection => projection.Section.Id);
        _pagesByTarget = pages.ToDictionary(page => page.Id.Value, StringComparer.OrdinalIgnoreCase);
        _menuItemsByTarget = _menuItemProjectionsById.Values.ToDictionary(
            projection => projection.Item.Id.Value,
            StringComparer.OrdinalIgnoreCase);
        _sectionsByTarget = sections.ToDictionary(section => section.Id.Value, StringComparer.OrdinalIgnoreCase);
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
        ValidateMenuContributions(menus);
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
        _pageProjectionsById.GetValueOrDefault(pageId)?.Page;

    public CompositionPageProjection? GetPageProjection(PageId pageId) =>
        _pageProjectionsById.GetValueOrDefault(pageId);

    public CompositionPageRegistration? GetPageByRoute(string route)
    {
        var normalizedRoute = NormalizeRoute(route);
        return _pagesByRoute.GetValueOrDefault(normalizedRoute);
    }

    public CompositionMenuRegistration? GetMenu(MenuId menuId) =>
        _menuProjectionsById.GetValueOrDefault(menuId)?.Menu;

    public CompositionMenuProjection? GetMenuProjection(MenuId menuId) =>
        _menuProjectionsById.GetValueOrDefault(menuId);

    public CompositionMenuItemRegistration? GetMenuItem(MenuItemId menuItemId) =>
        _menuItemProjectionsById.GetValueOrDefault(menuItemId)?.Item;

    public CompositionMenuItemProjection? GetMenuItemProjection(MenuItemId menuItemId) =>
        _menuItemProjectionsById.GetValueOrDefault(menuItemId);

    public IReadOnlyList<CompositionMenuItemProjection> GetMenuItemProjections(MenuId menuId) =>
        _menuItemProjectionsByMenu.GetValueOrDefault(menuId) ?? [];

    public CompositionSectionOutletRegistration? GetSectionOutlet(SectionOutletId outletId) =>
        _sectionOutletProjectionsById.GetValueOrDefault(outletId)?.Outlet;

    public CompositionSectionOutletProjection? GetSectionOutletProjection(SectionOutletId outletId) =>
        _sectionOutletProjectionsById.GetValueOrDefault(outletId);

    public IReadOnlyList<CompositionSectionRegistration> GetSections(
        PageId pageId,
        SectionOutletId outletId) =>
        _sectionsByOutlet.GetValueOrDefault((pageId, outletId)) ?? [];

    public IReadOnlyList<CompositionSectionProjection> GetSectionProjections(
        PageId pageId,
        SectionOutletId outletId) =>
        _sectionProjectionsByOutlet.GetValueOrDefault((pageId, outletId)) ?? [];

    public CompositionSectionProjection? GetSectionProjection(SectionId sectionId) =>
        _sectionProjectionsById.GetValueOrDefault(sectionId);

    public string ResolveHref(CompositionTarget target) =>
        ResolveHref(target, routeParams: null);

    public string ResolveHref(
        CompositionTarget target,
        IReadOnlyDictionary<string, object?>? routeParams)
    {
        if (target.Kind == CompositionTargetKind.Href)
        {
            return AppendRouteParams(target.Value, routeParams);
        }

        if (_menuItemsByTarget.TryGetValue(target.Value, out var menuItemProjection))
        {
            return ResolveHref(menuItemProjection.Item.Target, routeParams);
        }

        if (_pagesByTarget.TryGetValue(target.Value, out var page))
        {
            return AppendRouteParams(page.Route, routeParams);
        }

        if (_sectionsByTarget.TryGetValue(target.Value, out var section))
        {
            var sectionPage = GetPage(section.PageId);
            if (sectionPage is not null)
            {
                return $"{AppendRouteParams(sectionPage.Route, routeParams)}#{Uri.EscapeDataString(section.Id.Value)}";
            }
        }

        return IsDirectHref(target.Value)
            ? target.Value
            : "#";
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

    private static void ValidateMenuContributions(IReadOnlyList<CompositionMenuRegistration> menus)
    {
        ValidateUnique(menus.SelectMany(menu => menu.Groups).Select(group => group.Id.Value), "menu group");
        ValidateUnique(menus.SelectMany(GetMenuItems).Select(item => item.Id.Value), "menu item");

        foreach (var menu in menus)
        {
            var itemIds = GetMenuItems(menu)
                .Select(item => item.Id)
                .ToHashSet();

            foreach (var item in GetMenuItems(menu).Where(item => item.ParentId is not null))
            {
                if (!itemIds.Contains(item.ParentId!.Value))
                {
                    throw new InvalidOperationException(
                        $"Composition menu item '{item.Id}' targets unknown parent menu item '{item.ParentId}'.");
                }
            }
        }
    }

    private static IEnumerable<CompositionMenuItemRegistration> GetMenuItems(CompositionMenuRegistration menu) =>
        menu.Items.Concat(menu.Groups.SelectMany(group => group.Items));

    private static IEnumerable<CompositionMenuItemProjection> GetMenuItemProjections(CompositionModule module)
    {
        foreach (var menu in module.Menus)
        {
            foreach (var item in menu.Items)
            {
                yield return new CompositionMenuItemProjection(module.Id, menu, Group: null, item);
            }

            foreach (var group in menu.Groups)
            {
                foreach (var item in group.Items)
                {
                    yield return new CompositionMenuItemProjection(module.Id, menu, group, item);
                }
            }
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

    private static bool IsDirectHref(string value) =>
        value.StartsWith('/', StringComparison.Ordinal) ||
        value.StartsWith('#', StringComparison.Ordinal) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
