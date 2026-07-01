namespace CoreShell;

public readonly record struct CoreShellModuleId(string Value)
{
    public static CoreShellModuleId Create(string identifier) =>
        new(CoreShellIdFormatting.Normalize(identifier));

    public override string ToString() => Value;
}

public readonly record struct CoreShellMenuId(string Value)
{
    public static CoreShellMenuId Create(string identifier) =>
        new(CoreShellIdFormatting.Normalize(identifier));

    public override string ToString() => Value;
}

public readonly record struct CoreShellMenuGroupId(string Value)
{
    public static CoreShellMenuGroupId Create(CoreShellMenuId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public override string ToString() => Value;
}

public readonly record struct CoreShellMenuItemId(string Value)
{
    public static CoreShellMenuItemId Create(CoreShellMenuId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public static CoreShellMenuItemId Create(CoreShellMenuGroupId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public override string ToString() => Value;
}

public readonly record struct CoreShellPageId(string Value)
{
    public static CoreShellPageId Create(string identifier) =>
        new(CoreShellIdFormatting.Normalize(identifier));

    public override string ToString() => Value;

    public static implicit operator CoreShellTarget(CoreShellPageId id) =>
        CoreShellTarget.ForPage(id);
}

public readonly record struct CoreShellSectionOutletId(string Value)
{
    public static CoreShellSectionOutletId Create(CoreShellPageId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public static CoreShellSectionOutletId Create(CoreShellSectionId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public override string ToString() => Value;
}

public readonly record struct CoreShellSectionId(string Value)
{
    public static CoreShellSectionId Create(CoreShellPageId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public static CoreShellSectionId Create(CoreShellSectionOutletId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public static CoreShellSectionId Create(CoreShellSectionId parent, string identifier) =>
        new(CoreShellIdFormatting.CreateChild(parent.Value, identifier));

    public override string ToString() => Value;

    public static implicit operator CoreShellTarget(CoreShellSectionId id) =>
        CoreShellTarget.ForSection(id);
}

public enum CoreShellTargetKind
{
    Page = 0,
    Section = 1,
    Href = 2
}

public readonly record struct CoreShellTarget(
    string Value,
    CoreShellTargetKind Kind)
{
    public override string ToString() => Value;

    public static CoreShellTarget ForPage(CoreShellPageId id) =>
        new(id.Value, CoreShellTargetKind.Page);

    public static CoreShellTarget ForSection(CoreShellSectionId id) =>
        new(id.Value, CoreShellTargetKind.Section);

    public static CoreShellTarget ForHref(string href) =>
        new(CoreShellIdFormatting.NormalizeHref(href), CoreShellTargetKind.Href);
}

internal static class CoreShellIdFormatting
{
    public static string CreateChild(string parent, string identifier) =>
        $"{Normalize(parent)}.{Normalize(identifier)}";

    public static string Normalize(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("CoreShell identifiers cannot be empty.", nameof(identifier));
        }

        var normalized = identifier.Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("CoreShell identifiers cannot be empty.", nameof(identifier));
        }

        return normalized;
    }

    public static string NormalizeHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            throw new ArgumentException("CoreShell target hrefs cannot be empty.", nameof(href));
        }

        return href.Trim();
    }
}
