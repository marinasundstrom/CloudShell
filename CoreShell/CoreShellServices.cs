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

public interface ICoreShellSectionService
{
    Task<IReadOnlyList<CoreShellSectionOutletContribution>> GetSectionOutletsAsync(
        CoreShellPageId pageId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CoreShellSectionContribution>> GetSectionsAsync(
        CoreShellSectionOutletId outletId,
        CancellationToken cancellationToken = default);
}

public sealed class CoreShellModuleCatalog :
    ICoreShellPageService,
    ICoreShellMenuService,
    ICoreShellSectionService
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
