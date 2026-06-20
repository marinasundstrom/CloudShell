namespace CloudShell.UI.Composition;

public sealed class CompositionBuilder
{
    private readonly HashSet<SectionOutletId> _extendableOutlets = [];

    internal List<CompositionPageRegistration> Pages { get; } = [];

    internal List<CompositionMenuRegistration> Menus { get; } = [];

    internal List<CompositionSectionRegistration> Sections { get; } = [];

    public CompositionMenuBuilder AddMenu(MenuId id, string title)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));

        var menu = new CompositionMenuBuilder(this, id, title);
        Menus.Add(menu.Build());
        return menu;
    }

    public CompositionPageBuilder AddPage(PageId id, string title, string route)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));
        Pages.Add(new CompositionPageRegistration(id, title, NormalizeRoute(route)));
        return new CompositionPageBuilder(this, id);
    }

    public CompositionSectionOutletBuilder GetSections(SectionOutletId outletId)
    {
        if (!_extendableOutlets.Contains(outletId))
        {
            throw new InvalidOperationException($"Section outlet '{outletId}' is not registered as extendable.");
        }

        return new CompositionSectionOutletBuilder(this, FindOutletPage(outletId), outletId);
    }

    internal void RegisterExtendableOutlet(SectionOutletId outletId) =>
        _extendableOutlets.Add(outletId);

    internal CompositionRegistry Build() =>
        CompositionRegistry.FromBuilder(this);

    internal CompositionModule BuildModule(CompositionModuleId moduleId) =>
        new(
            moduleId,
            Pages.ToArray(),
            Menus.ToArray(),
            Sections.ToArray());

    private PageId FindOutletPage(SectionOutletId outletId)
    {
        var existing = Sections.FirstOrDefault(section => section.OutletId == outletId);
        if (existing is not null)
        {
            return existing.PageId;
        }

        throw new InvalidOperationException($"Section outlet '{outletId}' has not been attached to a page.");
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
    public CompositionSectionOutletBuilder AddSections(
        SectionOutletId outletId,
        bool allowExtending = false)
    {
        CompositionRegistry.ValidateId(outletId.Value, nameof(outletId));

        if (allowExtending)
        {
            builder.RegisterExtendableOutlet(outletId);
        }

        return new CompositionSectionOutletBuilder(builder, pageId, outletId);
    }
}

public sealed class CompositionSectionOutletBuilder(
    CompositionBuilder builder,
    PageId pageId,
    SectionOutletId outletId)
{
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
        int order)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));

        builder.Sections.Add(new CompositionSectionRegistration(
            id,
            pageId,
            outletId,
            title,
            component,
            order));

        return this;
    }
}

public sealed class CompositionMenuBuilder
{
    private readonly CompositionBuilder _builder;
    private readonly List<CompositionMenuItemRegistration> _items = [];
    private readonly List<CompositionMenuSectionBuilder> _sections = [];
    private readonly MenuId _id;
    private readonly string _title;

    internal CompositionMenuBuilder(
        CompositionBuilder builder,
        MenuId id,
        string title)
    {
        _builder = builder;
        _id = id;
        _title = title;
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

    public CompositionMenuSectionBuilder AddSection(
        MenuSectionId id,
        string title,
        int order)
    {
        var section = new CompositionMenuSectionBuilder(this, id, title, order);
        _sections.Add(section);
        ReplaceMenu();
        return section;
    }

    internal void ReplaceMenu()
    {
        var index = _builder.Menus.FindIndex(menu => menu.Id == _id);
        var registration = Build();
        if (index >= 0)
        {
            _builder.Menus[index] = registration;
        }
    }

    internal CompositionMenuRegistration Build() =>
        new(
            _id,
            _title,
            _items.OrderBy(item => item.Order).ToArray(),
            _sections.Select(section => section.Build()).OrderBy(section => section.Order).ToArray());
}

public sealed class CompositionMenuSectionBuilder(
    CompositionMenuBuilder menu,
    MenuSectionId id,
    string title,
    int order)
{
    private readonly List<CompositionMenuItemRegistration> _items = [];

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

    internal CompositionMenuSectionRegistration Build() =>
        new(
            id,
            title,
            _items.OrderBy(item => item.Order).ToArray(),
            order);
}

public sealed class CompositionMenuItemBuilder(
    Action<CompositionMenuItemRegistration> add,
    MenuItemId id,
    string title,
    int order)
{
    public void Target(PageId target) =>
        add(new CompositionMenuItemRegistration(id, title, target, order));

    public void Target(SectionId target) =>
        add(new CompositionMenuItemRegistration(id, title, target, order));
}
