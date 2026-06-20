namespace CloudShell.UiExtensionHost.Composition;

public sealed class SampleCompositionBuilder
{
    internal List<PageRegistration> Pages { get; } = [];

    internal List<MenuRegistration> Menus { get; } = [];

    internal List<SectionRegistration> Sections { get; } = [];

    private readonly HashSet<SectionOutletId> _extendableOutlets = [];

    public SampleMenuBuilder AddMenu(MenuId id, string title)
    {
        SampleCompositionRegistry.ValidateId(id.Value, nameof(id));

        var menu = new SampleMenuBuilder(this, id, title);
        Menus.Add(menu.Build());
        return menu;
    }

    public SamplePageBuilder AddPage(PageId id, string title, string route)
    {
        SampleCompositionRegistry.ValidateId(id.Value, nameof(id));
        Pages.Add(new PageRegistration(id, title, NormalizeRoute(route)));
        return new SamplePageBuilder(this, id);
    }

    public SampleSectionOutletBuilder GetSections(SectionOutletId outletId)
    {
        if (!_extendableOutlets.Contains(outletId))
        {
            throw new InvalidOperationException($"Section outlet '{outletId}' is not registered as extendable.");
        }

        return new SampleSectionOutletBuilder(this, FindOutletPage(outletId), outletId);
    }

    internal void RegisterExtendableOutlet(SectionOutletId outletId) =>
        _extendableOutlets.Add(outletId);

    internal SampleCompositionRegistry Build() =>
        SampleCompositionRegistry.FromBuilder(this);

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
        SampleCompositionRegistry.ValidateId(route, nameof(route));
        return route.StartsWith('/') ? route : "/" + route;
    }
}

public sealed class SamplePageBuilder(
    SampleCompositionBuilder builder,
    PageId pageId)
{
    public SampleSectionOutletBuilder AddSections(
        SectionOutletId outletId,
        bool allowExtending = false)
    {
        SampleCompositionRegistry.ValidateId(outletId.Value, nameof(outletId));

        if (allowExtending)
        {
            builder.RegisterExtendableOutlet(outletId);
        }

        return new SampleSectionOutletBuilder(builder, pageId, outletId);
    }
}

public sealed class SampleSectionOutletBuilder(
    SampleCompositionBuilder builder,
    PageId pageId,
    SectionOutletId outletId)
{
    public SampleSectionOutletBuilder AddSection<TComponent>(
        SectionId id,
        string title,
        int order)
    {
        AddSection(id, title, typeof(TComponent), order);
        return this;
    }

    public SampleSectionOutletBuilder AddSection(
        SectionId id,
        string title,
        Type component,
        int order)
    {
        SampleCompositionRegistry.ValidateId(id.Value, nameof(id));

        builder.Sections.Add(new SectionRegistration(
            id,
            pageId,
            outletId,
            title,
            component,
            order));

        return this;
    }
}

public sealed class SampleMenuBuilder
{
    private readonly SampleCompositionBuilder _builder;
    private readonly List<MenuItemRegistration> _items = [];
    private readonly List<SampleMenuSectionBuilder> _sections = [];
    private readonly MenuId _id;
    private readonly string _title;

    internal SampleMenuBuilder(
        SampleCompositionBuilder builder,
        MenuId id,
        string title)
    {
        _builder = builder;
        _id = id;
        _title = title;
    }

    public SampleMenuItemBuilder AddItem(
        MenuItemId id,
        string title,
        int order)
    {
        var item = new SampleMenuItemBuilder(registration =>
        {
            _items.Add(registration);
            ReplaceMenu();
        }, id, title, order);
        return item;
    }

    public SampleMenuSectionBuilder AddSection(
        MenuSectionId id,
        string title,
        int order)
    {
        var section = new SampleMenuSectionBuilder(this, id, title, order);
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

    internal MenuRegistration Build() =>
        new(
            _id,
            _title,
            _items.OrderBy(item => item.Order).ToArray(),
            _sections.Select(section => section.Build()).OrderBy(section => section.Order).ToArray());
}

public sealed class SampleMenuSectionBuilder(
    SampleMenuBuilder menu,
    MenuSectionId id,
    string title,
    int order)
{
    private readonly List<MenuItemRegistration> _items = [];

    public SampleMenuItemBuilder AddItem(
        MenuItemId itemId,
        string itemTitle,
        int itemOrder)
    {
        var item = new SampleMenuItemBuilder(registration =>
        {
            _items.Add(registration);
            menu.ReplaceMenu();
        }, itemId, itemTitle, itemOrder);
        return item;
    }

    internal MenuSectionRegistration Build() =>
        new(
            id,
            title,
            _items.OrderBy(item => item.Order).ToArray(),
            order);
}

public sealed class SampleMenuItemBuilder(
    Action<MenuItemRegistration> add,
    MenuItemId id,
    string title,
    int order)
{
    public void Target(PageId target) =>
        add(new MenuItemRegistration(id, title, target, order));

    public void Target(SectionId target) =>
        add(new MenuItemRegistration(id, title, target, order));
}
