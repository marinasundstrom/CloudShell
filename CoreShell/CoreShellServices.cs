namespace CoreShell;

public interface ICoreShellPageService
{
    Task<IReadOnlyList<CoreShellPageContribution>> GetPagesAsync(
        CancellationToken cancellationToken = default);

    Task<CoreShellPageContribution?> GetPageAsync(
        CoreShellPageId pageId,
        CancellationToken cancellationToken = default);
}

public interface ICoreShellMenuService
{
    Task<IReadOnlyList<CoreShellMenuContribution>> GetMenusAsync(
        CancellationToken cancellationToken = default);

    Task<CoreShellMenuContribution?> GetMenuAsync(
        CoreShellMenuId menuId,
        CancellationToken cancellationToken = default);
}

public interface ICoreShellNavigationService : ICoreShellMenuService;

public interface ICoreShellSectionService
{
    Task<IReadOnlyList<CoreShellSectionOutletContribution>> GetSectionOutletsAsync(
        CoreShellPageId pageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoreShellSectionContribution>> GetSectionsAsync(
        CoreShellSectionOutletId outletId,
        CancellationToken cancellationToken = default);
}

public interface ICoreShellSectionAddressService
{
    string GetSectionAddressValue(
        CoreShellSectionOutletContribution outlet,
        CoreShellSectionContribution section);

    bool TryResolveSectionRequest(
        CoreShellSectionOutletContribution outlet,
        IReadOnlyList<CoreShellSectionContribution> sections,
        string requested,
        out CoreShellSectionContribution section);
}

public interface ICoreShellRouteService
{
    Task<CoreShellPageContribution?> GetPageByRouteAsync(
        string route,
        CancellationToken cancellationToken = default);

    Task<CoreShellResolvedTarget> ResolveTargetAsync(
        CoreShellTarget target,
        IReadOnlyDictionary<string, object?>? routeValues = null,
        CancellationToken cancellationToken = default);
}

public sealed record CoreShellResolvedTarget(
    CoreShellTarget Target,
    string Href,
    CoreShellPageContribution? Page = null,
    CoreShellSectionContribution? Section = null);

public interface ICoreShellPageResolver
{
    Task<CoreShellResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default);
}

public interface ICoreShellPageResolutionService
{
    Task<CoreShellResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record CoreShellPageResolutionContext(
    string Route,
    IReadOnlyDictionary<string, object?>? RouteValues = null);

public sealed record CoreShellResolvedPage(
    CoreShellPageContribution Page,
    IReadOnlyList<CoreShellSectionOutletContribution> SectionOutlets,
    IReadOnlyList<CoreShellSectionContribution> Sections);

public sealed class CoreShellPageResolutionService(
    IEnumerable<ICoreShellPageResolver> resolvers) : ICoreShellPageResolutionService
{
    public async Task<CoreShellResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        foreach (var resolver in resolvers)
        {
            var page = await resolver.ResolvePageAsync(context, cancellationToken);
            if (page is not null)
            {
                return page;
            }
        }

        return null;
    }
}

public sealed class CoreShellSectionAddressService : ICoreShellSectionAddressService
{
    public string GetSectionAddressValue(
        CoreShellSectionOutletContribution outlet,
        CoreShellSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        ArgumentNullException.ThrowIfNull(section);

        return CoreShellRouteProjection.GetSectionSelectionValue(section, outlet);
    }

    public bool TryResolveSectionRequest(
        CoreShellSectionOutletContribution outlet,
        IReadOnlyList<CoreShellSectionContribution> sections,
        string requested,
        out CoreShellSectionContribution section)
    {
        ArgumentNullException.ThrowIfNull(outlet);
        ArgumentNullException.ThrowIfNull(sections);

        section = null!;
        if (string.IsNullOrWhiteSpace(requested))
        {
            return false;
        }

        var normalizedRequest = requested.Trim();
        var requestedSection = sections.FirstOrDefault(section =>
            string.Equals(section.Id.Value, normalizedRequest, StringComparison.OrdinalIgnoreCase));
        if (requestedSection is not null)
        {
            section = requestedSection;
            return true;
        }

        var matchingSections = sections
            .Where(item => string.Equals(
                GetSectionAddressValue(outlet, item),
                normalizedRequest,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingSections.Length != 1)
        {
            return false;
        }

        section = matchingSections[0];
        return true;
    }
}

public sealed class CoreShellModuleCatalog :
    ICoreShellPageService,
    ICoreShellNavigationService,
    ICoreShellSectionService,
    ICoreShellRouteService,
    ICoreShellPageResolver
{
    private readonly IReadOnlyList<CoreShellPageContribution> _pages;
    private readonly IReadOnlyList<CoreShellMenuContribution> _menus;
    private readonly IReadOnlyList<CoreShellSectionOutletContribution> _sectionOutlets;
    private readonly IReadOnlyList<CoreShellSectionContribution> _sections;

    public CoreShellModuleCatalog(IEnumerable<CoreShellModule> modules)
    {
        ArgumentNullException.ThrowIfNull(modules);

        var moduleList = modules.ToArray();
        _pages = UniqueBy(
            moduleList.SelectMany(module => module.Pages),
            page => page.Id.Value,
            "CoreShell page");
        _sectionOutlets = UniqueBy(
            moduleList.SelectMany(module => module.SectionOutlets),
            outlet => outlet.Id.Value,
            "CoreShell section outlet");
        _sections = UniqueBy(
                moduleList.SelectMany(module => module.Sections),
                section => section.Id.Value,
                "CoreShell section")
            .OrderBy(section => section.Order)
            .ThenBy(section => section.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _menus = MergeMenus(moduleList.SelectMany(module => module.Menus));
    }

    public Task<IReadOnlyList<CoreShellPageContribution>> GetPagesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_pages);

    public Task<CoreShellPageContribution?> GetPageAsync(
        CoreShellPageId pageId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_pages.FirstOrDefault(page => page.Id == pageId));

    public Task<CoreShellPageContribution?> GetPageByRouteAsync(
        string route,
        CancellationToken cancellationToken = default)
    {
        var normalizedRoute = CoreShellRouteProjection.NormalizeRoute(route);
        var page = _pages.FirstOrDefault(page =>
            string.Equals(page.Route, normalizedRoute, StringComparison.OrdinalIgnoreCase))
            ?? _pages
                .Where(page => CoreShellRouteProjection.RouteTemplateMatches(page.Route, normalizedRoute))
                .OrderByDescending(page => CoreShellRouteProjection.GetRouteTemplateSpecificity(page.Route))
                .FirstOrDefault();

        return Task.FromResult(page);
    }

    public Task<IReadOnlyList<CoreShellMenuContribution>> GetMenusAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_menus);

    public Task<CoreShellMenuContribution?> GetMenuAsync(
        CoreShellMenuId menuId,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(_menus.FirstOrDefault(menu => menu.Id == menuId));

    public Task<IReadOnlyList<CoreShellSectionOutletContribution>> GetSectionOutletsAsync(
        CoreShellPageId pageId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CoreShellSectionOutletContribution> result = _sectionOutlets
            .Where(outlet => outlet.PageId == pageId)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<CoreShellSectionContribution>> GetSectionsAsync(
        CoreShellSectionOutletId outletId,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<CoreShellSectionContribution> result = _sections
            .Where(section => section.OutletId == outletId)
            .ToArray();
        return Task.FromResult(result);
    }

    public Task<CoreShellResolvedTarget> ResolveTargetAsync(
        CoreShellTarget target,
        IReadOnlyDictionary<string, object?>? routeValues = null,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(ResolveTarget(target, routeValues));

    public async Task<CoreShellResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var page = await GetPageByRouteAsync(context.Route, cancellationToken);
        if (page is null)
        {
            return null;
        }

        var outlets = _sectionOutlets
            .Where(outlet => outlet.PageId == page.Id)
            .ToArray();
        var sections = _sections
            .Where(section => section.PageId == page.Id)
            .ToArray();

        return new(page, outlets, sections);
    }

    private CoreShellResolvedTarget ResolveTarget(
        CoreShellTarget target,
        IReadOnlyDictionary<string, object?>? routeValues)
    {
        if (target.Kind == CoreShellTargetKind.Href)
        {
            return new(target, CoreShellRouteProjection.AppendRouteValues(target.Value, routeValues));
        }

        if (target.Kind == CoreShellTargetKind.Page &&
            _pages.FirstOrDefault(page => string.Equals(
                page.Id.Value,
                target.Value,
                StringComparison.OrdinalIgnoreCase)) is { } page)
        {
            return new(
                target,
                CoreShellRouteProjection.AppendRouteValues(page.Route, routeValues),
                Page: page);
        }

        if (target.Kind == CoreShellTargetKind.Section &&
            _sections.FirstOrDefault(section => string.Equals(
                section.Id.Value,
                target.Value,
                StringComparison.OrdinalIgnoreCase)) is { } section &&
            _pages.FirstOrDefault(page => page.Id == section.PageId) is { } sectionPage &&
            _sectionOutlets.FirstOrDefault(outlet => outlet.Id == section.OutletId) is { } sectionOutlet)
        {
            return new(
                target,
                CoreShellRouteProjection.ResolveSectionHref(sectionPage, sectionOutlet, section, routeValues),
                Page: sectionPage,
                Section: section);
        }

        return new(
            target,
            CoreShellRouteProjection.IsDirectHref(target.Value)
                ? target.Value
                : "#");
    }

    private static IReadOnlyList<T> UniqueBy<T>(
        IEnumerable<T> values,
        Func<T, string> keySelector,
        string artifactName)
    {
        var results = new List<T>();
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            var key = keySelector(value);
            if (!keys.Add(key))
            {
                throw new InvalidOperationException($"Duplicate {artifactName} ID '{key}'.");
            }

            results.Add(value);
        }

        return results;
    }

    private static IReadOnlyList<CoreShellMenuContribution> MergeMenus(
        IEnumerable<CoreShellMenuContribution> menus) =>
        menus
            .GroupBy(menu => menu.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select(MergeMenuGroup)
            .OrderBy(menu => menu.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static CoreShellMenuContribution MergeMenuGroup(
        IGrouping<string, CoreShellMenuContribution> menus)
    {
        var first = menus.First();
        var rootItems = UniqueBy(
                menus.SelectMany(menu => menu.Items),
                item => item.Id.Value,
                "CoreShell menu item")
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var groups = menus
            .SelectMany(menu => menu.Groups)
            .GroupBy(group => group.Id.Value, StringComparer.OrdinalIgnoreCase)
            .Select(MergeMenuItemGroup)
            .OrderBy(group => group.Order)
            .ThenBy(group => group.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return first with
        {
            Items = rootItems,
            Groups = groups
        };
    }

    private static CoreShellMenuGroupContribution MergeMenuItemGroup(
        IGrouping<string, CoreShellMenuGroupContribution> groups)
    {
        var first = groups.First();
        var items = UniqueBy(
                groups.SelectMany(group => group.Items),
                item => item.Id.Value,
                "CoreShell menu item")
            .OrderBy(item => item.Order)
            .ThenBy(item => item.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return first with { Items = items };
    }
}
