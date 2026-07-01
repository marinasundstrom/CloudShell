using System.Globalization;
using System.Text;

namespace CoreShell.Composition;

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
        _menus = MergeMenus(menus);
        _sectionOutlets = sectionOutlets;
        _sections = sections;
        _pageProjectionsById = modules
            .SelectMany(module => module.Pages.Select(page => new CompositionPageProjection(module.Id, page)))
            .ToDictionary(projection => projection.Page.Id);
        _pagesByRoute = pages.ToDictionary(page => page.Route, StringComparer.OrdinalIgnoreCase);
        _menuProjectionsById = _menus
            .Select(menu => new CompositionMenuProjection(GetMenuOwner(modules, menu.Id), menu))
            .ToDictionary(projection => projection.Menu.Id);
        _menuItemProjectionsById = modules
            .SelectMany(module => GetMenuItemProjections(module, _menuProjectionsById))
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
        ValidateMenuContributions(menus);
        ValidateUnique(sectionOutlets.Select(outlet => outlet.Id.Value), "section outlet");
        ValidateUnique(sections.Select(section => section.Id.Value), "section");
        ValidateSectionOutletContributions(materializedModules);
        ValidateSectionContributions(materializedModules);
        ValidateSectionAddressModes(sectionOutlets, sections);

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
        if (_pagesByRoute.TryGetValue(normalizedRoute, out var exactPage))
        {
            return exactPage;
        }

        return _pages
            .Where(page => RouteTemplateMatches(page.Route, normalizedRoute))
            .OrderByDescending(page => GetRouteTemplateSpecificity(page.Route))
            .FirstOrDefault();
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

    public string? GetSectionAddressValue(SectionId sectionId)
    {
        if (!_sectionsByTarget.TryGetValue(sectionId.Value, out var section) ||
            GetSectionOutlet(section.OutletId) is not { } outlet)
        {
            return null;
        }

        return GetSectionSelectionValue(section, outlet);
    }

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
            var sectionOutlet = GetSectionOutlet(section.OutletId);
            if (sectionPage is not null && sectionOutlet is not null)
            {
                return ResolveSectionHref(sectionPage, sectionOutlet, section, routeParams);
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
        var queryStart = IndexOfRouteSuffix(normalized);
        return queryStart >= 0 ? normalized[..queryStart] : normalized;
    }

    private static int IndexOfRouteSuffix(string route)
    {
        var templateDepth = 0;
        for (var index = 0; index < route.Length; index++)
        {
            var value = route[index];
            if (value == '{')
            {
                templateDepth++;
                continue;
            }

            if (value == '}' && templateDepth > 0)
            {
                templateDepth--;
                continue;
            }

            if (templateDepth == 0 && (value == '?' || value == '#'))
            {
                return index;
            }
        }

        return -1;
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
        foreach (var menu in menus)
        {
            ValidateUnique(menu.Groups.Select(group => group.Id.Value), "menu group");
        }

        foreach (var menuContributions in menus.GroupBy(menu => menu.Id))
        {
            var contributedItemIds = menuContributions
                .SelectMany(GetMenuItems)
                .Select(item => item.Id)
                .ToArray();
            ValidateUnique(contributedItemIds.Select(id => id.Value), "menu item");
            var itemIds = contributedItemIds.ToHashSet();

            foreach (var item in menuContributions.SelectMany(GetMenuItems).Where(item => item.ParentId is not null))
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

    private static IReadOnlyList<CompositionMenuRegistration> MergeMenus(IReadOnlyList<CompositionMenuRegistration> menus) =>
        menus
            .GroupBy(menu => menu.Id)
            .Select(MergeMenu)
            .ToArray();

    private static CompositionMenuRegistration MergeMenu(IGrouping<MenuId, CompositionMenuRegistration> menus)
    {
        var menuList = menus.ToArray();
        var title = menuList
            .Select(menu => menu.Title)
            .FirstOrDefault(title => !string.IsNullOrWhiteSpace(title))
            ?? menus.Key.Value;
        var authorization = menuList
            .Select(menu => menu.Authorization)
            .FirstOrDefault(authorization => !authorization.IsEmpty)
            ?? CompositionAuthorizationRequirements.None;
        var rootItems = menuList
            .SelectMany(menu => menu.Items)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groups = menuList
            .SelectMany(menu => menu.Groups)
            .GroupBy(group => group.Id)
            .Select(MergeMenuGroup)
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new(menus.Key, title, rootItems, groups, authorization);
    }

    private static CompositionMenuGroupRegistration MergeMenuGroup(
        IGrouping<MenuGroupId, CompositionMenuGroupRegistration> groups)
    {
        var groupList = groups.ToArray();
        var first = groupList[0];
        var authorization = groupList
            .Select(group => group.Authorization)
            .FirstOrDefault(authorization => !authorization.IsEmpty)
            ?? CompositionAuthorizationRequirements.None;
        var items = groupList
            .SelectMany(group => group.Items)
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return first with { Items = items, Authorization = authorization };
    }

    private static CompositionModuleId GetMenuOwner(
        IReadOnlyList<CompositionModule> modules,
        MenuId menuId) =>
        modules.First(module => module.Menus.Any(menu => menu.Id == menuId)).Id;

    private static IEnumerable<CompositionMenuItemProjection> GetMenuItemProjections(
        CompositionModule module,
        IReadOnlyDictionary<MenuId, CompositionMenuProjection> menuProjections)
    {
        foreach (var menu in module.Menus)
        {
            var mergedMenu = menuProjections[menu.Id].Menu;
            foreach (var item in menu.Items)
            {
                yield return new CompositionMenuItemProjection(module.Id, mergedMenu, Group: null, item);
            }

            foreach (var group in menu.Groups)
            {
                var mergedGroup = mergedMenu.Groups.FirstOrDefault(mergedGroup => mergedGroup.Id == group.Id) ?? group;
                foreach (var item in group.Items)
                {
                    yield return new CompositionMenuItemProjection(module.Id, mergedMenu, mergedGroup, item);
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

    private static void ValidateSectionAddressModes(
        IReadOnlyList<CompositionSectionOutletRegistration> sectionOutlets,
        IReadOnlyList<CompositionSectionRegistration> sections)
    {
        var outletsById = sectionOutlets.ToDictionary(outlet => outlet.Id);
        foreach (var group in sections.GroupBy(section => section.OutletId))
        {
            if (!outletsById.TryGetValue(group.Key, out var outlet) ||
                outlet.AddressMode != CompositionSectionAddressMode.Child)
            {
                continue;
            }

            if (FindParentSection(outlet, sections) is { } parentSection &&
                outletsById.TryGetValue(parentSection.OutletId, out var parentOutlet) &&
                parentOutlet.AddressMode == CompositionSectionAddressMode.Parent)
            {
                throw new InvalidOperationException(
                    $"Composition section outlet '{outlet.Id}' cannot use child addresses because its parent section '{parentSection.Id}' shares its parent address.");
            }

            var duplicate = group
                .GroupBy(section => GetSectionSelectionValue(section, outlet), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(candidate => candidate.Count() > 1);
            if (duplicate is not null)
            {
                throw new InvalidOperationException(
                    $"Composition section outlet '{outlet.Id}' uses child addresses, but selection value '{duplicate.Key}' is used by multiple sections.");
            }
        }
    }

    private static CompositionSectionRegistration? FindParentSection(
        CompositionSectionOutletRegistration outlet,
        IReadOnlyList<CompositionSectionRegistration> sections) =>
        sections
            .OrderByDescending(section => section.Id.Value.Length)
            .FirstOrDefault(section => IsSectionOutletChildOfSection(outlet, section));

    private static bool IsSectionOutletChildOfSection(
        CompositionSectionOutletRegistration outlet,
        CompositionSectionRegistration section)
    {
        var sectionScope = StripCompositionKind("section", section.Id.Value);
        var childOutletPrefix = $"section-outlet.{sectionScope}.";
        return outlet.Id.Value.StartsWith(childOutletPrefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string ResolveSectionHref(
        CompositionPageRegistration sectionPage,
        CompositionSectionOutletRegistration sectionOutlet,
        CompositionSectionRegistration section,
        IReadOnlyDictionary<string, object?>? routeParams)
    {
        if (sectionOutlet.AddressMode == CompositionSectionAddressMode.Child)
        {
            return AppendRouteParams(
                sectionPage.Route,
                WithRouteParameter(
                    routeParams,
                    sectionOutlet.SelectionKey,
                    GetSectionSelectionValue(section, sectionOutlet)));
        }

        return $"{AppendRouteParams(sectionPage.Route, routeParams)}#{Uri.EscapeDataString(section.Id.Value)}";
    }

    private static IReadOnlyDictionary<string, object?> WithRouteParameter(
        IReadOnlyDictionary<string, object?>? routeParams,
        string name,
        string value)
    {
        var merged = routeParams is null
            ? new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
            : routeParams
                .Where(parameter => !string.Equals(parameter.Key, name, StringComparison.OrdinalIgnoreCase))
                .ToDictionary(parameter => parameter.Key, parameter => parameter.Value, StringComparer.OrdinalIgnoreCase);

        merged[name] = value;
        return merged;
    }

    private static string GetSectionSelectionValue(
        CompositionSectionRegistration section,
        CompositionSectionOutletRegistration outlet)
    {
        var outletScope = StripCompositionKind("section-outlet", outlet.Id.Value);
        var outletScopedPrefix = $"section.{outletScope}.";
        if (section.Id.Value.StartsWith(outletScopedPrefix, StringComparison.OrdinalIgnoreCase) &&
            section.Id.Value.Length > outletScopedPrefix.Length)
        {
            return section.Id.Value[outletScopedPrefix.Length..];
        }

        var pageScope = StripCompositionKind("page", section.PageId.Value);
        var pageScopedPrefix = $"section.{pageScope}.";
        if (section.Id.Value.StartsWith(pageScopedPrefix, StringComparison.OrdinalIgnoreCase) &&
            section.Id.Value.Length > pageScopedPrefix.Length)
        {
            return section.Id.Value[pageScopedPrefix.Length..];
        }

        var lastSeparator = section.Id.Value.LastIndexOf('.');
        return lastSeparator >= 0 && lastSeparator + 1 < section.Id.Value.Length
            ? section.Id.Value[(lastSeparator + 1)..]
            : section.Id.Value;
    }

    private static string StripCompositionKind(string kind, string value)
    {
        var prefix = kind + ".";
        return value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            ? value[prefix.Length..]
            : value;
    }

    private static string AppendRouteParams(
        string route,
        IReadOnlyDictionary<string, object?>? routeParams)
    {
        if ((routeParams is null || routeParams.Count == 0) &&
            !route.Contains('{', StringComparison.Ordinal))
        {
            return route;
        }

        routeParams ??= EmptyRouteParams;

        var usedParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var materializedRoute = TryMaterializeRouteTemplate(route, routeParams, usedParameters);
        if (materializedRoute is null)
        {
            return "#";
        }

        var query = string.Join(
            '&',
            routeParams
                .Where(parameter =>
                    parameter.Value is not null &&
                    !usedParameters.Contains(parameter.Key))
                .Select(parameter =>
                    $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(Convert.ToString(parameter.Value, CultureInfo.InvariantCulture) ?? string.Empty)}"));

        if (string.IsNullOrEmpty(query))
        {
            return materializedRoute;
        }

        return AppendQuery(materializedRoute, query);
    }

    private static string? TryMaterializeRouteTemplate(
        string route,
        IReadOnlyDictionary<string, object?> routeParams,
        ISet<string> usedParameters)
    {
        var result = new StringBuilder(route.Length);
        var cursor = 0;

        while (cursor < route.Length)
        {
            var openBrace = route.IndexOf('{', cursor);
            if (openBrace < 0)
            {
                result.Append(route, cursor, route.Length - cursor);
                return result.ToString();
            }

            var closeBrace = route.IndexOf('}', openBrace + 1);
            if (closeBrace < 0)
            {
                result.Append(route, cursor, route.Length - cursor);
                return result.ToString();
            }

            var token = route[(openBrace + 1)..closeBrace];
            var parameter = ParseRouteTemplateParameter(token);
            var parameterName = parameter.Name;
            if (string.IsNullOrWhiteSpace(parameterName))
            {
                result.Append(route, cursor, openBrace - cursor);
                result.Append(route, openBrace, closeBrace - openBrace + 1);
                cursor = closeBrace + 1;
                continue;
            }

            var routeParameter = routeParams.FirstOrDefault(candidate =>
                string.Equals(candidate.Key, parameterName, StringComparison.OrdinalIgnoreCase));
            if (routeParameter.Key is null || routeParameter.Value is null)
            {
                if (parameter.IsOptional && TryAppendWithoutOptionalSegment(route, result, cursor, openBrace, closeBrace))
                {
                    cursor = closeBrace + 1;
                    continue;
                }

                return null;
            }

            result.Append(route, cursor, openBrace - cursor);
            usedParameters.Add(routeParameter.Key);
            result.Append(Uri.EscapeDataString(
                Convert.ToString(routeParameter.Value, CultureInfo.InvariantCulture) ?? string.Empty));
            cursor = closeBrace + 1;
        }

        return result.ToString();
    }

    private static bool TryAppendWithoutOptionalSegment(
        string route,
        StringBuilder result,
        int cursor,
        int openBrace,
        int closeBrace)
    {
        var prefixLength = openBrace - cursor;
        if (prefixLength <= 0)
        {
            return false;
        }

        var prefixEnd = openBrace - 1;
        var next = closeBrace + 1 < route.Length
            ? route[closeBrace + 1]
            : '\0';
        var isSegmentParameter =
            route[prefixEnd] == '/' &&
            (next == '\0' || next == '?' || next == '#');

        if (!isSegmentParameter)
        {
            return false;
        }

        result.Append(route, cursor, prefixLength - 1);
        return true;
    }

    private static RouteTemplateParameter ParseRouteTemplateParameter(string token)
    {
        var trimmed = token.Trim().TrimStart('*');
        var end = trimmed.IndexOfAny([':', '?']);
        var name = end < 0
            ? trimmed
            : trimmed[..end];
        return new(name, trimmed.Contains('?', StringComparison.Ordinal));
    }

    private static bool RouteTemplateMatches(string template, string route)
    {
        var templateSegments = GetRouteSegments(template);
        var routeSegments = GetRouteSegments(route);
        var routeIndex = 0;

        for (var templateIndex = 0; templateIndex < templateSegments.Length; templateIndex++)
        {
            var templateSegment = templateSegments[templateIndex];
            if (TryParseSegmentParameter(templateSegment, out var parameter))
            {
                if (routeIndex >= routeSegments.Length)
                {
                    if (parameter.IsOptional)
                    {
                        continue;
                    }

                    return false;
                }

                routeIndex++;
                continue;
            }

            if (routeIndex >= routeSegments.Length ||
                !string.Equals(
                    templateSegment,
                    routeSegments[routeIndex],
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            routeIndex++;
        }

        return routeIndex == routeSegments.Length;
    }

    private static int GetRouteTemplateSpecificity(string template)
    {
        var segments = GetRouteSegments(template);
        var literalSegments = segments.Count(segment => !TryParseSegmentParameter(segment, out _));
        var requiredParameters = segments.Count(segment =>
            TryParseSegmentParameter(segment, out var parameter) &&
            !parameter.IsOptional);
        return (literalSegments * 100) + (requiredParameters * 10) + segments.Length;
    }

    private static string[] GetRouteSegments(string route)
    {
        var normalized = NormalizeRoute(route);
        return normalized == "/"
            ? []
            : normalized.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool TryParseSegmentParameter(
        string segment,
        out RouteTemplateParameter parameter)
    {
        parameter = default;
        if (segment.Length < 2 ||
            segment[0] != '{' ||
            segment[^1] != '}')
        {
            return false;
        }

        parameter = ParseRouteTemplateParameter(segment[1..^1]);
        return !string.IsNullOrWhiteSpace(parameter.Name);
    }

    private readonly record struct RouteTemplateParameter(string Name, bool IsOptional);

    private static string AppendQuery(string route, string query)
    {
        var fragmentStart = route.IndexOf('#');
        var routeWithoutFragment = fragmentStart < 0
            ? route
            : route[..fragmentStart];
        var fragment = fragmentStart < 0
            ? string.Empty
            : route[fragmentStart..];
        var separator = routeWithoutFragment.Contains('?')
            ? "&"
            : "?";

        return $"{routeWithoutFragment}{separator}{query}{fragment}";
    }

    private static bool IsDirectHref(string value) =>
        value.StartsWith('/', StringComparison.Ordinal) ||
        value.StartsWith('#', StringComparison.Ordinal) ||
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    private static readonly IReadOnlyDictionary<string, object?> EmptyRouteParams =
        new Dictionary<string, object?>();
}
