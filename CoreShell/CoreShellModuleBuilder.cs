namespace CoreShell;

public sealed class CoreShellModuleBuilder
{
    private readonly List<CoreShellPageContribution> _pages = [];
    private readonly List<CoreShellMenuBuilder> _menus = [];
    private readonly List<CoreShellSectionOutletContribution> _sectionOutlets = [];
    private readonly List<CoreShellSectionContribution> _sections = [];

    public CoreShellModuleBuilder(CoreShellModuleId id)
    {
        Id = id.Value.Length == 0
            ? throw new ArgumentException("CoreShell module identifiers cannot be empty.", nameof(id))
            : id;
    }

    public CoreShellModuleId Id { get; }

    public CoreShellPageBuilder AddPage(
        CoreShellPageId id,
        string title,
        string route,
        bool isExtendable = false,
        CoreShellAuthorizationRequirements? authorization = null)
    {
        _pages.Add(new CoreShellPageContribution(
            id,
            title,
            NormalizeRoute(route),
            isExtendable,
            authorization));

        return new CoreShellPageBuilder(this, id);
    }

    public CoreShellMenuBuilder AddMenu(
        CoreShellMenuId id,
        string title,
        CoreShellAuthorizationRequirements? authorization = null)
    {
        var menu = new CoreShellMenuBuilder(id, title, authorization);
        _menus.Add(menu);
        return menu;
    }

    internal void AddSectionOutlet(CoreShellSectionOutletContribution outlet) =>
        _sectionOutlets.Add(outlet);

    internal void AddSection(CoreShellSectionContribution section) =>
        _sections.Add(section);

    public CoreShellModule Build() =>
        new(
            Id,
            _pages.ToArray(),
            _menus.Select(menu => menu.Build()).ToArray(),
            _sectionOutlets.ToArray(),
            _sections.OrderBy(section => section.Order).ToArray());

    private static string NormalizeRoute(string route)
    {
        if (string.IsNullOrWhiteSpace(route))
        {
            throw new ArgumentException("CoreShell page routes cannot be empty.", nameof(route));
        }

        var trimmed = route.Trim();
        return trimmed.StartsWith('/', StringComparison.Ordinal)
            ? trimmed
            : "/" + trimmed;
    }
}

public sealed class CoreShellPageBuilder(
    CoreShellModuleBuilder builder,
    CoreShellPageId pageId)
{
    public CoreShellSectionOutletBuilder AddSections(
        CoreShellSectionOutletId outletId,
        bool isExtendable = false,
        CoreShellAuthorizationRequirements? authorization = null,
        CoreShellSectionAddressMode addressMode = CoreShellSectionAddressMode.Parent,
        string? selectionKey = null)
    {
        builder.AddSectionOutlet(new CoreShellSectionOutletContribution(
            outletId,
            pageId,
            isExtendable,
            authorization,
            addressMode,
            selectionKey));

        return new CoreShellSectionOutletBuilder(builder, pageId, outletId);
    }
}

public sealed class CoreShellSectionOutletBuilder(
    CoreShellModuleBuilder builder,
    CoreShellPageId pageId,
    CoreShellSectionOutletId outletId)
{
    public CoreShellSectionOutletBuilder AddSection(
        CoreShellSectionId id,
        string title,
        CoreShellContentReference content,
        int order,
        CoreShellAuthorizationRequirements? authorization = null,
        IReadOnlyDictionary<string, string>? attributes = null)
    {
        builder.AddSection(new CoreShellSectionContribution(
            id,
            pageId,
            outletId,
            title,
            content,
            order,
            authorization,
            attributes));

        return this;
    }
}

public sealed class CoreShellMenuBuilder(
    CoreShellMenuId id,
    string title,
    CoreShellAuthorizationRequirements? authorization = null)
{
    private readonly List<CoreShellMenuItemContribution> _items = [];
    private readonly List<CoreShellMenuGroupBuilder> _groups = [];

    public CoreShellMenuItemBuilder AddItem(
        CoreShellMenuItemId itemId,
        string itemTitle,
        int itemOrder) =>
        new(registration => _items.Add(registration), itemId, itemTitle, itemOrder);

    public CoreShellMenuGroupBuilder AddGroup(
        CoreShellMenuGroupId groupId,
        string groupTitle,
        int groupOrder,
        CoreShellAuthorizationRequirements? groupAuthorization = null)
    {
        var group = new CoreShellMenuGroupBuilder(groupId, groupTitle, groupOrder, groupAuthorization);
        _groups.Add(group);
        return group;
    }

    internal CoreShellMenuContribution Build() =>
        new(
            id,
            title,
            _items.OrderBy(item => item.Order).ToArray(),
            _groups.Select(group => group.Build()).OrderBy(group => group.Order).ToArray(),
            authorization);
}

public sealed class CoreShellMenuGroupBuilder(
    CoreShellMenuGroupId id,
    string title,
    int order,
    CoreShellAuthorizationRequirements? authorization = null)
{
    private readonly List<CoreShellMenuItemContribution> _items = [];

    public CoreShellMenuItemBuilder AddItem(
        CoreShellMenuItemId itemId,
        string itemTitle,
        int itemOrder) =>
        new(registration => _items.Add(registration), itemId, itemTitle, itemOrder);

    internal CoreShellMenuGroupContribution Build() =>
        new(id, title, _items.OrderBy(item => item.Order).ToArray(), order, authorization);
}

public sealed class CoreShellMenuItemBuilder(
    Action<CoreShellMenuItemContribution> add,
    CoreShellMenuItemId id,
    string title,
    int order)
{
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private CoreShellAuthorizationRequirements _authorization = CoreShellAuthorizationRequirements.None;
    private CoreShellMenuItemId? _parentId;

    public CoreShellMenuItemBuilder WithAttribute(string name, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (string.IsNullOrWhiteSpace(value))
        {
            _attributes.Remove(name.Trim());
        }
        else
        {
            _attributes[name.Trim()] = value.Trim();
        }

        return this;
    }

    public CoreShellMenuItemBuilder WithParent(CoreShellMenuItemId parentId)
    {
        _parentId = parentId;
        return this;
    }

    public CoreShellMenuItemBuilder RequiresPermissions(params string[] permissions)
    {
        _authorization = CoreShellAuthorizationRequirements.FromAnyPermissions(permissions);
        return this;
    }

    public void Target(CoreShellPageId target) =>
        Target(CoreShellTarget.ForPage(target));

    public void Target(CoreShellSectionId target) =>
        Target(CoreShellTarget.ForSection(target));

    public void TargetHref(string href) =>
        Target(CoreShellTarget.ForHref(href));

    public void Target(CoreShellTarget target) =>
        add(new CoreShellMenuItemContribution(
            id,
            title,
            target,
            order,
            _attributes.Count == 0
                ? null
                : new Dictionary<string, string>(_attributes, StringComparer.OrdinalIgnoreCase),
            _parentId,
            _authorization));
}
