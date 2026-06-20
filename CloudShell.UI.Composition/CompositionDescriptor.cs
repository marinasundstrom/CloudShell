namespace CloudShell.UI.Composition;

public sealed record CompositionModuleDescriptor(
    CompositionModuleId Id,
    IReadOnlyList<CompositionPageDescriptor> Pages,
    IReadOnlyList<CompositionMenuDescriptor> Menus,
    IReadOnlyList<CompositionSectionOutletDescriptor> SectionOutlets,
    IReadOnlyList<CompositionSectionDescriptor> Sections);

public sealed record CompositionPageDescriptor(
    PageId Id,
    string Title,
    string Route,
    bool IsExtendable = false);

public sealed record CompositionMenuDescriptor(
    MenuId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemDescriptor> Items,
    IReadOnlyList<CompositionMenuSectionDescriptor> Sections);

public sealed record CompositionMenuSectionDescriptor(
    MenuSectionId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemDescriptor> Items,
    int Order);

public sealed record CompositionMenuItemDescriptor(
    MenuItemId Id,
    string Title,
    CompositionTarget Target,
    int Order);

public sealed record CompositionSectionOutletDescriptor(
    SectionOutletId Id,
    PageId PageId,
    bool IsExtendable);

public sealed record CompositionSectionDescriptor(
    SectionId Id,
    PageId PageId,
    SectionOutletId OutletId,
    string Title,
    string ComponentTypeName,
    int Order);

public static class CompositionDescriptorExtensions
{
    public static CompositionModuleDescriptor ToDescriptor(this CompositionModule module) =>
        new(
            module.Id,
            module.Pages.Select(page => page.ToDescriptor()).ToArray(),
            module.Menus.Select(menu => menu.ToDescriptor()).ToArray(),
            module.SectionOutlets.Select(outlet => outlet.ToDescriptor()).ToArray(),
            module.Sections.Select(section => section.ToDescriptor()).ToArray());

    public static CompositionPageDescriptor ToDescriptor(this CompositionPageRegistration page) =>
        new(page.Id, page.Title, page.Route, page.IsExtendable);

    public static CompositionMenuDescriptor ToDescriptor(this CompositionMenuRegistration menu) =>
        new(
            menu.Id,
            menu.Title,
            menu.Items.Select(item => item.ToDescriptor()).ToArray(),
            menu.Sections.Select(section => section.ToDescriptor()).ToArray());

    public static CompositionMenuSectionDescriptor ToDescriptor(this CompositionMenuSectionRegistration section) =>
        new(
            section.Id,
            section.Title,
            section.Items.Select(item => item.ToDescriptor()).ToArray(),
            section.Order);

    public static CompositionMenuItemDescriptor ToDescriptor(this CompositionMenuItemRegistration item) =>
        new(item.Id, item.Title, item.Target, item.Order);

    public static CompositionSectionOutletDescriptor ToDescriptor(this CompositionSectionOutletRegistration outlet) =>
        new(outlet.Id, outlet.PageId, outlet.IsExtendable);

    public static CompositionSectionDescriptor ToDescriptor(this CompositionSectionRegistration section) =>
        new(
            section.Id,
            section.PageId,
            section.OutletId,
            section.Title,
            section.ComponentType.AssemblyQualifiedName ?? section.ComponentType.FullName ?? section.ComponentType.Name,
            section.Order);
}
