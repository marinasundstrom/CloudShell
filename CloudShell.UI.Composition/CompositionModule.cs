namespace CloudShell.UI.Composition;

public sealed record CompositionModule(
    CompositionModuleId Id,
    IReadOnlyList<CompositionPageRegistration> Pages,
    IReadOnlyList<CompositionMenuRegistration> Menus,
    IReadOnlyList<CompositionSectionOutletRegistration> SectionOutlets,
    IReadOnlyList<CompositionSectionRegistration> Sections)
{
    public static CompositionModule Create(
        CompositionModuleId id,
        Action<CompositionModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        CompositionRegistry.ValidateId(id.Value, nameof(id));

        var builder = new CompositionModuleBuilder(id);
        configure(builder);
        return builder.Build();
    }

    public static CompositionModule FromDescriptor(
        CompositionModuleDescriptor descriptor,
        ICompositionComponentTypeResolver componentTypes)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ArgumentNullException.ThrowIfNull(componentTypes);

        CompositionRegistry.ValidateId(descriptor.Id.Value, nameof(descriptor));

        return new CompositionModule(
            descriptor.Id,
            descriptor.Pages.Select(page => new CompositionPageRegistration(
                page.Id,
                page.Title,
                page.Route,
                page.IsExtendable)).ToArray(),
            descriptor.Menus.Select(menu => new CompositionMenuRegistration(
                menu.Id,
                menu.Title,
                menu.Items.Select(ToMenuItemRegistration).ToArray(),
                menu.Sections.Select(section => new CompositionMenuSectionRegistration(
                    section.Id,
                    section.Title,
                    section.Items.Select(ToMenuItemRegistration).ToArray(),
                    section.Order)).ToArray())).ToArray(),
            descriptor.SectionOutlets.Select(outlet => new CompositionSectionOutletRegistration(
                outlet.Id,
                outlet.PageId,
                outlet.IsExtendable)).ToArray(),
            descriptor.Sections.Select(section => new CompositionSectionRegistration(
                section.Id,
                section.PageId,
                section.OutletId,
                section.Title,
                componentTypes.ResolveComponentType(section.ComponentTypeName),
                section.Order)).ToArray());
    }

    private static CompositionMenuItemRegistration ToMenuItemRegistration(CompositionMenuItemDescriptor item) =>
        new(item.Id, item.Title, item.Target, item.Order);
}

public interface ICompositionComponentTypeResolver
{
    Type ResolveComponentType(string componentTypeName);
}

public sealed class CompositionModuleBuilder
{
    private readonly CompositionBuilder _builder = new();

    public CompositionModuleBuilder(CompositionModuleId id)
    {
        CompositionRegistry.ValidateId(id.Value, nameof(id));
        Id = id;
    }

    public CompositionModuleId Id { get; }

    public CompositionMenuBuilder AddMenu(MenuId id, string title) =>
        _builder.AddMenu(id, title);

    public CompositionPageBuilder AddPage(
        PageId id,
        string title,
        string route,
        bool isExtendable = false) =>
        _builder.AddPage(id, title, route, isExtendable);

    public CompositionPageExtensionBuilder Extend(PageId pageId) =>
        _builder.Extend(pageId);

    public CompositionSectionOutletBuilder GetSections(SectionOutletId outletId) =>
        _builder.GetSections(outletId);

    public CompositionSectionOutletBuilder GetSections(
        PageId pageId,
        SectionOutletId outletId) =>
        _builder.GetSections(pageId, outletId);

    public CompositionSectionOutletExtensionBuilder Extend(
        CompositionSectionOutletExtensionPoint outlet) =>
        _builder.Extend(outlet);

    public CompositionModule Build() =>
        _builder.BuildModule(Id);
}
