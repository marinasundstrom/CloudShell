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
    bool IsExtendable = false,
    CompositionAuthorizationRequirements? Authorization = null);

public sealed record CompositionMenuDescriptor(
    MenuId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemDescriptor> Items,
    IReadOnlyList<CompositionMenuGroupDescriptor> Groups,
    CompositionAuthorizationRequirements? Authorization = null);

public sealed record CompositionMenuGroupDescriptor(
    MenuGroupId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemDescriptor> Items,
    int Order,
    CompositionAuthorizationRequirements? Authorization = null);

public sealed record CompositionMenuItemDescriptor(
    MenuItemId Id,
    string Title,
    CompositionTarget Target,
    int Order,
    IReadOnlyDictionary<string, string>? Attributes = null,
    MenuItemId? ParentId = null,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record CompositionSectionOutletDescriptor(
    SectionOutletId Id,
    PageId PageId,
    bool IsExtendable,
    CompositionAuthorizationRequirements? Authorization = null,
    CompositionSectionAddressMode AddressMode = CompositionSectionAddressMode.Parent,
    string? SelectionKey = null);

public sealed record CompositionSectionDescriptor(
    SectionId Id,
    PageId PageId,
    SectionOutletId OutletId,
    string Title,
    string ComponentTypeName,
    int Order,
    CompositionAuthorizationRequirements? Authorization = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

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
        new(page.Id, page.Title, page.Route, page.IsExtendable, page.Authorization);

    public static CompositionMenuDescriptor ToDescriptor(this CompositionMenuRegistration menu) =>
        new(
            menu.Id,
            menu.Title,
            menu.Items.Select(item => item.ToDescriptor()).ToArray(),
            menu.Groups.Select(group => group.ToDescriptor()).ToArray(),
            menu.Authorization);

    public static CompositionMenuGroupDescriptor ToDescriptor(this CompositionMenuGroupRegistration group) =>
        new(
            group.Id,
            group.Title,
            group.Items.Select(item => item.ToDescriptor()).ToArray(),
            group.Order,
            group.Authorization);

    public static CompositionMenuItemDescriptor ToDescriptor(this CompositionMenuItemRegistration item) =>
        new(
            item.Id,
            item.Title,
            item.Target,
            item.Order,
            item.Attributes,
            item.ParentId,
            item.Authorization);

    public static CompositionSectionOutletDescriptor ToDescriptor(this CompositionSectionOutletRegistration outlet) =>
        new(
            outlet.Id,
            outlet.PageId,
            outlet.IsExtendable,
            outlet.Authorization,
            outlet.AddressMode,
            outlet.SelectionKey);

    public static CompositionSectionDescriptor ToDescriptor(this CompositionSectionRegistration section) =>
        new(
            section.Id,
            section.PageId,
            section.OutletId,
            section.Title,
            section.ComponentType.AssemblyQualifiedName ?? section.ComponentType.FullName ?? section.ComponentType.Name,
            section.Order,
            section.Authorization,
            section.Attributes);
}
