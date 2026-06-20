namespace CloudShell.UiExtensionHost.Composition;

public readonly record struct MenuId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MenuSectionId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct MenuItemId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct PageId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator CompositionTarget(PageId id) =>
        CompositionTarget.ForPage(id);
}

public readonly record struct SectionOutletId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SectionId(string Value)
{
    public override string ToString() => Value;

    public static implicit operator CompositionTarget(SectionId id) =>
        CompositionTarget.ForSection(id);
}

public readonly record struct CompositionTarget(string Value)
{
    public override string ToString() => Value;

    public static CompositionTarget ForPage(PageId id) =>
        new(id.Value);

    public static CompositionTarget ForSection(SectionId id) =>
        new(id.Value);
}
