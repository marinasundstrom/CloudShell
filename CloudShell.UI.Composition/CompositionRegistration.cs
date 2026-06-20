namespace CloudShell.UI.Composition;

public sealed record CompositionPageRegistration(
    PageId Id,
    string Title,
    string Route,
    bool IsExtendable = false,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;
}

public sealed record CompositionMenuRegistration(
    MenuId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemRegistration> Items,
    IReadOnlyList<CompositionMenuGroupRegistration> Groups,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;
}

public sealed record CompositionMenuGroupRegistration(
    MenuGroupId Id,
    string Title,
    IReadOnlyList<CompositionMenuItemRegistration> Items,
    int Order,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;
}

public sealed record CompositionMenuItemRegistration(
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

    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;

    public IReadOnlyList<string> PermissionsRequiredForNavigation => Authorization.AnyPermissions;
}

public sealed record CompositionSectionOutletRegistration(
    SectionOutletId Id,
    PageId PageId,
    bool IsExtendable,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;
}

public sealed record CompositionSectionRegistration(
    SectionId Id,
    PageId PageId,
    SectionOutletId OutletId,
    string Title,
    Type ComponentType,
    int Order,
    CompositionAuthorizationRequirements? Authorization = null)
{
    public CompositionAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CompositionAuthorizationRequirements.None;
}

public sealed record CompositionContext(
    PageId PageId,
    string Route);
