namespace CloudShell.UI.Composition;

public sealed record CompositionPageRegistration(
    PageId Id,
    string Title,
    string Route);

public sealed record CompositionMenuRegistration(
    MenuId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemRegistration> Items,
    IReadOnlyList<CompositionMenuSectionRegistration> Sections);

public sealed record CompositionMenuSectionRegistration(
    MenuSectionId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemRegistration> Items,
    int Order);

public sealed record CompositionMenuItemRegistration(
    MenuItemId Id,
    string Title,
    CompositionTarget Target,
    int Order);

public sealed record CompositionSectionOutletRegistration(
    SectionOutletId Id,
    PageId PageId,
    bool IsExtendable);

public sealed record CompositionSectionRegistration(
    SectionId Id,
    PageId PageId,
    SectionOutletId OutletId,
    string Title,
    Type ComponentType,
    int Order);

public sealed record CompositionContext(
    PageId PageId,
    string Route);
