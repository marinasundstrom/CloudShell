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
}

public readonly record struct SectionOutletId(string Value)
{
    public override string ToString() => Value;
}

public readonly record struct SectionId(string Value)
{
    public override string ToString() => Value;
}

