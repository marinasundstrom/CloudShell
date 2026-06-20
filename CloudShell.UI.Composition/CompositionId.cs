namespace CloudShell.UI.Composition;

public readonly record struct CompositionModuleId(string Value)
{
    public static readonly CompositionModuleId Host = new("composition-module.host");

    public static CompositionModuleId Create(string identifier) =>
        new(CompositionIdFormatting.CreateRoot("composition-module", identifier));

    public override string ToString() => Value;
}

public readonly record struct MenuId(string Value)
{
    public static MenuId Create(string identifier) =>
        new(CompositionIdFormatting.CreateRoot("menu", identifier));

    public override string ToString() => Value;
}

public readonly record struct MenuGroupId(string Value)
{
    public static MenuGroupId Create(MenuId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("menu-group", "menu", parent.Value, identifier));

    public override string ToString() => Value;
}

public readonly record struct MenuItemId(string Value)
{
    public static MenuItemId Create(MenuId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("menu-item", "menu", parent.Value, identifier));

    public static MenuItemId Create(MenuGroupId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("menu-item", "menu-group", parent.Value, identifier));

    public override string ToString() => Value;

    public static implicit operator CompositionTarget(MenuItemId id) =>
        CompositionTarget.ForMenuItem(id);
}

public readonly record struct PageId(string Value)
{
    public static PageId Create(string identifier) =>
        new(CompositionIdFormatting.CreateRoot("page", identifier));

    public override string ToString() => Value;

    public static implicit operator CompositionTarget(PageId id) =>
        CompositionTarget.ForPage(id);
}

public readonly record struct SectionOutletId(string Value)
{
    public static SectionOutletId Create(PageId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("section-outlet", "page", parent.Value, identifier));

    public static SectionOutletId Create(SectionId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("section-outlet", "section", parent.Value, identifier));

    public override string ToString() => Value;
}

public readonly record struct CompositionSectionOutletExtensionPoint(
    PageId PageId,
    SectionOutletId OutletId);

public readonly record struct SectionId(string Value)
{
    public static SectionId Create(PageId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("section", "page", parent.Value, identifier));

    public static SectionId Create(SectionOutletId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("section", "section-outlet", parent.Value, identifier));

    public static SectionId Create(SectionId parent, string identifier) =>
        new(CompositionIdFormatting.CreateChild("section", "section", parent.Value, identifier));

    public override string ToString() => Value;

    public static implicit operator CompositionTarget(SectionId id) =>
        CompositionTarget.ForSection(id);
}

public enum CompositionTargetKind
{
    Artifact = 0,
    Href = 1
}

public readonly record struct CompositionTarget(
    string Value,
    CompositionTargetKind Kind = CompositionTargetKind.Artifact)
{
    public override string ToString() => Value;

    public static CompositionTarget ForPage(PageId id) =>
        new(id.Value, CompositionTargetKind.Artifact);

    public static CompositionTarget ForSection(SectionId id) =>
        new(id.Value, CompositionTargetKind.Artifact);

    public static CompositionTarget ForMenuItem(MenuItemId id) =>
        new(id.Value, CompositionTargetKind.Artifact);

    public static CompositionTarget ForHref(string href)
    {
        if (string.IsNullOrWhiteSpace(href))
        {
            throw new ArgumentException("Composition target hrefs cannot be empty.", nameof(href));
        }

        return new(href.Trim(), CompositionTargetKind.Href);
    }
}

internal static class CompositionIdFormatting
{
    public static string CreateRoot(string kind, string identifier) =>
        $"{kind}.{NormalizeIdentifier(identifier)}";

    public static string CreateChild(
        string kind,
        string parentKind,
        string parentValue,
        string identifier) =>
        $"{kind}.{StripKind(parentKind, parentValue)}.{NormalizeIdentifier(identifier)}";

    private static string NormalizeIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Composition ID identifiers cannot be empty.", nameof(identifier));
        }

        var normalized = identifier.Trim().Trim('.');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("Composition ID identifiers cannot be empty.", nameof(identifier));
        }

        return normalized;
    }

    private static string StripKind(string kind, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Composition ID values cannot be empty.", nameof(value));
        }

        var prefix = kind + ".";
        return value.StartsWith(prefix, StringComparison.Ordinal)
            ? value[prefix.Length..]
            : value;
    }
}
