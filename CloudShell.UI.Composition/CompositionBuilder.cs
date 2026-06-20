namespace CloudShell.UI.Composition;

public sealed class CompositionBuilder
{
    internal List<CompositionPageRegistration> Pages { get; } = [];

    internal List<CompositionMenuRegistration> Menus { get; } = [];

    internal List<CompositionSectionOutletRegistration> SectionOutlets { get; } = [];

    internal List<CompositionSectionRegistration> Sections { get; } = [];

    public CompositionMenuBuilder AddMenu(
        MenuId id,
        string title,
        CompositionAuthorizationRequirements? authorization = null)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));

        var menu = new CompositionMenuBuilder(this, id, title, authorization);
        menu.Register();
        return menu;
    }

    public CompositionMenuBuilder GetMenu(MenuId id)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));

        var menu = new CompositionMenuBuilder(this, id, title: string.Empty);
        menu.Register();
        return menu;
    }

    public CompositionPageBuilder AddPage(
        PageId id,
        string title,
        string route,
        bool isExtendable = false,
        CompositionAuthorizationRequirements? authorization = null)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));
        Pages.Add(new CompositionPageRegistration(id, title, NormalizeRoute(route), isExtendable, authorization));
        return new CompositionPageBuilder(this, id);
    }

    public CompositionPageExtensionBuilder Extend(PageId pageId)
    {
        CompositionRegistry.ValidateId(pageId.Value, nameof(pageId));
        return new CompositionPageExtensionBuilder(this, pageId);
    }

    public CompositionSectionOutletBuilder GetSections(SectionOutletId outletId)
    {
        var outlet = SectionOutlets.FirstOrDefault(outlet => outlet.Id == outletId);
        if (outlet is null || !outlet.IsExtendable)
        {
            throw new InvalidOperationException($"Section outlet '{outletId}' is not registered as extendable.");
        }

        return new CompositionSectionOutletBuilder(this, outlet.PageId, outletId);
    }

    public CompositionSectionOutletExtensionBuilder Extend(
        CompositionSectionOutletExtensionPoint outlet) =>
        new(this, outlet.PageId, outlet.OutletId);

    internal CompositionSectionOutletBuilder GetSections(
        PageId pageId,
        SectionOutletId outletId)
    {
        CompositionRegistry.ValidateId(pageId.Value, nameof(pageId));
        CompositionRegistry.ValidateId(outletId.Value, nameof(outletId));

        return new CompositionSectionOutletBuilder(this, pageId, outletId);
    }

    internal CompositionRegistry Build() =>
        CompositionRegistry.FromBuilder(this);

    internal CompositionModule BuildModule(CompositionModuleId moduleId) =>
        new(
            moduleId,
            Pages.ToArray(),
            Menus.ToArray(),
            SectionOutlets.ToArray(),
            Sections.ToArray());

    internal void AddSection(
        PageId pageId,
        SectionOutletId outletId,
        SectionId sectionId,
        string title,
        Type component,
        int order,
        CompositionAuthorizationRequirements? authorization = null)
    {
        CompositionRegistry.ValidateId(sectionId.Value, nameof(sectionId));

        Sections.Add(new CompositionSectionRegistration(
            sectionId,
            pageId,
            outletId,
            title,
            component,
            order,
            authorization));
    }

    internal void ReplacePageAuthorization(
        PageId pageId,
        CompositionAuthorizationRequirements authorization)
    {
        var index = Pages.FindIndex(page => page.Id == pageId);
        if (index >= 0)
        {
            Pages[index] = Pages[index] with { Authorization = authorization };
        }
    }

    internal void ReplaceSectionOutletAddressMode(
        PageId pageId,
        SectionOutletId outletId,
        CompositionSectionAddressMode addressMode,
        string? selectionKey)
    {
        var index = SectionOutlets.FindIndex(outlet =>
            outlet.PageId == pageId &&
            outlet.Id == outletId);
        if (index >= 0)
        {
            SectionOutlets[index] = SectionOutlets[index] with
            {
                AddressMode = addressMode,
                SelectionKey = selectionKey ?? CompositionSectionOutletRegistration.DefaultSelectionKey
            };
        }
    }

    private static string NormalizeRoute(string route)
    {
        CompositionRegistry.ValidateId(route, nameof(route));
        return CompositionRegistry.NormalizeRoute(route);
    }
}

public sealed class CompositionPageBuilder(
    CompositionBuilder builder,
    PageId pageId)
{
    public CompositionPageBuilder RequiresPermissions(params string[] permissions) =>
        RequiresAuthorization(CompositionAuthorizationRequirements.FromAnyPermissions(permissions));

    public CompositionPageBuilder RequiresAuthorization(CompositionAuthorizationRequirements authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        builder.ReplacePageAuthorization(pageId, authorization);
        return this;
    }

    public CompositionSectionOutletBuilder AddSections(
        SectionOutletId outletId,
        bool isExtendable = false,
        CompositionAuthorizationRequirements? authorization = null,
        CompositionSectionAddressMode addressMode = CompositionSectionAddressMode.Parent,
        string? selectionKey = null)
    {
        CompositionRegistry.ValidateId(outletId.Value, nameof(outletId));

        builder.SectionOutlets.Add(new CompositionSectionOutletRegistration(
            outletId,
            pageId,
            isExtendable,
            authorization,
            addressMode,
            selectionKey));

        return new CompositionSectionOutletBuilder(builder, pageId, outletId);
    }
}

public sealed class CompositionPageExtensionBuilder(
    CompositionBuilder builder,
    PageId pageId)
{
    public CompositionSectionOutletBuilder AddSections(
        SectionOutletId outletId,
        bool isExtendable = false,
        CompositionAuthorizationRequirements? authorization = null,
        CompositionSectionAddressMode addressMode = CompositionSectionAddressMode.Parent,
        string? selectionKey = null)
    {
        CompositionRegistry.ValidateId(outletId.Value, nameof(outletId));

        builder.SectionOutlets.Add(new CompositionSectionOutletRegistration(
            outletId,
            pageId,
            isExtendable,
            authorization,
            addressMode,
            selectionKey));

        return new CompositionSectionOutletBuilder(builder, pageId, outletId);
    }
}

public sealed class CompositionSectionOutletBuilder(
    CompositionBuilder builder,
    PageId pageId,
    SectionOutletId outletId)
{
    public CompositionSectionOutletBuilder UseChildAddresses(
        string selectionKey = CompositionSectionOutletRegistration.DefaultSelectionKey)
    {
        builder.ReplaceSectionOutletAddressMode(
            pageId,
            outletId,
            CompositionSectionAddressMode.Child,
            selectionKey);
        return this;
    }

    public CompositionSectionOutletBuilder UseParentAddress()
    {
        builder.ReplaceSectionOutletAddressMode(
            pageId,
            outletId,
            CompositionSectionAddressMode.Parent,
            CompositionSectionOutletRegistration.DefaultSelectionKey);
        return this;
    }

    public CompositionSectionOutletBuilder AddSection<TComponent>(
        SectionId id,
        string title,
        int order)
    {
        AddSection(id, title, typeof(TComponent), order);
        return this;
    }

    public CompositionSectionOutletBuilder AddSection(
        SectionId id,
        string title,
        Type component,
        int order,
        CompositionAuthorizationRequirements? authorization = null)
    {
        builder.AddSection(pageId, outletId, id, title, component, order, authorization);

        return this;
    }
}

public sealed class CompositionSectionOutletExtensionBuilder(
    CompositionBuilder builder,
    PageId pageId,
    SectionOutletId outletId)
{
    public CompositionSectionOutletExtensionBuilder AddSection<TComponent>(
        SectionId id,
        string title,
        int order)
    {
        AddSection(id, title, typeof(TComponent), order);
        return this;
    }

    public CompositionSectionOutletExtensionBuilder AddSection(
        SectionId id,
        string title,
        Type component,
        int order,
        CompositionAuthorizationRequirements? authorization = null)
    {
        builder.AddSection(pageId, outletId, id, title, component, order, authorization);

        return this;
    }
}

public sealed class CompositionMenuBuilder
{
    private readonly CompositionBuilder _builder;
    private readonly List<CompositionMenuItemRegistration> _items = [];
    private readonly List<CompositionMenuGroupBuilder> _groups = [];
    private readonly MenuId _id;
    private readonly string _title;
    private CompositionAuthorizationRequirements _authorization;
    private int? _menuIndex;

    internal CompositionMenuBuilder(
        CompositionBuilder builder,
        MenuId id,
        string title,
        CompositionAuthorizationRequirements? authorization = null)
    {
        _builder = builder;
        _id = id;
        _title = title;
        _authorization = authorization ?? CompositionAuthorizationRequirements.None;
    }

    public CompositionMenuBuilder RequiresPermissions(params string[] permissions) =>
        RequiresAuthorization(CompositionAuthorizationRequirements.FromAnyPermissions(permissions));

    public CompositionMenuBuilder RequiresAuthorization(CompositionAuthorizationRequirements authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        _authorization = authorization;
        ReplaceMenu();
        return this;
    }

    public CompositionMenuItemBuilder AddItem(
        MenuItemId id,
        string title,
        int order)
    {
        var item = new CompositionMenuItemBuilder(registration =>
        {
            _items.Add(registration);
            ReplaceMenu();
        }, id, title, order);
        return item;
    }

    public CompositionMenuGroupBuilder AddGroup(
        MenuGroupId id,
        string title,
        int order,
        CompositionAuthorizationRequirements? authorization = null)
    {
        var group = new CompositionMenuGroupBuilder(this, id, title, order, authorization);
        _groups.Add(group);
        ReplaceMenu();
        return group;
    }

    internal void ReplaceMenu()
    {
        if (_menuIndex is null)
        {
            Register();
            return;
        }

        var index = _menuIndex.Value;
        var registration = Build();
        if (index < _builder.Menus.Count)
        {
            _builder.Menus[index] = registration;
            return;
        }

        Register();
    }

    internal void Register()
    {
        _builder.Menus.Add(Build());
        _menuIndex = _builder.Menus.Count - 1;
    }

    internal CompositionMenuRegistration Build() =>
        new(
            _id,
            _title,
            _items.OrderBy(item => item.Order).ToArray(),
            _groups.Select(group => group.Build()).OrderBy(group => group.Order).ToArray(),
            _authorization);
}

public sealed class CompositionMenuGroupBuilder(
    CompositionMenuBuilder menu,
    MenuGroupId id,
    string title,
    int order,
    CompositionAuthorizationRequirements? authorization = null)
{
    private readonly List<CompositionMenuItemRegistration> _items = [];
    private CompositionAuthorizationRequirements _authorization =
        authorization ?? CompositionAuthorizationRequirements.None;

    public CompositionMenuGroupBuilder RequiresPermissions(params string[] permissions) =>
        RequiresAuthorization(CompositionAuthorizationRequirements.FromAnyPermissions(permissions));

    public CompositionMenuGroupBuilder RequiresAuthorization(CompositionAuthorizationRequirements authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        _authorization = authorization;
        menu.ReplaceMenu();
        return this;
    }

    public CompositionMenuItemBuilder AddItem(
        MenuItemId itemId,
        string itemTitle,
        int itemOrder)
    {
        var item = new CompositionMenuItemBuilder(registration =>
        {
            _items.Add(registration);
            menu.ReplaceMenu();
        }, itemId, itemTitle, itemOrder);
        return item;
    }

    internal CompositionMenuGroupRegistration Build() =>
        new(
            id,
            title,
            _items.OrderBy(item => item.Order).ToArray(),
            order,
            _authorization);
}

public sealed class CompositionMenuItemBuilder(
    Action<CompositionMenuItemRegistration> add,
    MenuItemId id,
    string title,
    int order)
{
    private readonly Dictionary<string, string> _attributes = new(StringComparer.OrdinalIgnoreCase);
    private MenuItemId? _parentId;
    private CompositionAuthorizationRequirements _authorization = CompositionAuthorizationRequirements.None;

    public CompositionMenuItemBuilder WithAttribute(string name, string? value)
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

    public CompositionMenuItemBuilder WithAttributes(IReadOnlyDictionary<string, string>? attributes)
    {
        if (attributes is null)
        {
            return this;
        }

        foreach (var attribute in attributes)
        {
            WithAttribute(attribute.Key, attribute.Value);
        }

        return this;
    }

    public CompositionMenuItemBuilder WithParent(MenuItemId parentId)
    {
        CompositionRegistry.ValidateId(parentId.Value, nameof(parentId));
        _parentId = parentId;
        return this;
    }

    public CompositionMenuItemBuilder RequiresPermissions(params string[] permissions) =>
        RequiresPermissions((IReadOnlyList<string>?)permissions);

    public CompositionMenuItemBuilder RequiresPermissions(IReadOnlyList<string>? permissions)
    {
        _authorization = CompositionAuthorizationRequirements.FromAnyPermissions(permissions);
        return this;
    }

    public CompositionMenuItemBuilder RequiresAuthorization(CompositionAuthorizationRequirements authorization)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        _authorization = authorization;
        return this;
    }

    public void Target(PageId target) =>
        Target(CompositionTarget.ForPage(target));

    public void Target(SectionId target) =>
        Target(CompositionTarget.ForSection(target));

    public void TargetHref(string href) =>
        Target(CompositionTarget.ForHref(href));

    public void Target(CompositionTarget target) =>
        add(new CompositionMenuItemRegistration(
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
