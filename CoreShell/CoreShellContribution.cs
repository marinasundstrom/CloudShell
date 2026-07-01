namespace CoreShell;

public sealed record CoreShellModule(
    CoreShellModuleId Id,
    IReadOnlyList<CoreShellPageContribution> Pages,
    IReadOnlyList<CoreShellMenuContribution> Menus,
    IReadOnlyList<CoreShellSectionOutletContribution> SectionOutlets,
    IReadOnlyList<CoreShellSectionContribution> Sections)
{
    public static CoreShellModule Create(
        CoreShellModuleId id,
        Action<CoreShellModuleBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var builder = new CoreShellModuleBuilder(id);
        configure(builder);
        return builder.Build();
    }
}

public sealed record CoreShellPageContribution(
    CoreShellPageId Id,
    string Title,
    string Route,
    bool IsExtendable = false,
    CoreShellAuthorizationRequirements? Authorization = null)
{
    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;
}

public sealed record CoreShellMenuContribution(
    CoreShellMenuId Id,
    string Title,
    IReadOnlyList<CoreShellMenuItemContribution> Items,
    IReadOnlyList<CoreShellMenuGroupContribution> Groups,
    CoreShellAuthorizationRequirements? Authorization = null)
{
    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;
}

public sealed record CoreShellMenuGroupContribution(
    CoreShellMenuGroupId Id,
    string Title,
    IReadOnlyList<CoreShellMenuItemContribution> Items,
    int Order,
    CoreShellAuthorizationRequirements? Authorization = null)
{
    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;
}

public sealed record CoreShellMenuItemContribution(
    CoreShellMenuItemId Id,
    string Title,
    CoreShellTarget Target,
    int Order,
    IReadOnlyDictionary<string, string>? Attributes = null,
    CoreShellMenuItemId? ParentId = null,
    CoreShellAuthorizationRequirements? Authorization = null)
{
    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;
}

public sealed record CoreShellSectionOutletContribution(
    CoreShellSectionOutletId Id,
    CoreShellPageId PageId,
    bool IsExtendable,
    CoreShellAuthorizationRequirements? Authorization = null,
    CoreShellSectionAddressMode AddressMode = CoreShellSectionAddressMode.Parent,
    string? SelectionKey = null)
{
    public const string DefaultSelectionKey = "section";

    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;

    public string SelectionKey { get; init; } =
        string.IsNullOrWhiteSpace(SelectionKey)
            ? DefaultSelectionKey
            : SelectionKey.Trim();
}

public enum CoreShellSectionAddressMode
{
    Parent = 0,
    Child = 1
}

public sealed record CoreShellSectionContribution(
    CoreShellSectionId Id,
    CoreShellPageId PageId,
    CoreShellSectionOutletId OutletId,
    string Title,
    CoreShellContentReference Content,
    int Order,
    CoreShellAuthorizationRequirements? Authorization = null,
    IReadOnlyDictionary<string, string>? Attributes = null)
{
    public CoreShellAuthorizationRequirements Authorization { get; init; } =
        Authorization ?? CoreShellAuthorizationRequirements.None;

    public IReadOnlyDictionary<string, string> Attributes { get; init; } =
        Attributes ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}
