namespace CloudShell.UI.Composition;

public sealed record CompositionModule(
    CompositionModuleId Id,
    IReadOnlyList<CompositionPageRegistration> Pages,
    IReadOnlyList<CompositionMenuRegistration> Menus,
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

    public CompositionPageBuilder AddPage(PageId id, string title, string route) =>
        _builder.AddPage(id, title, route);

    public CompositionSectionOutletBuilder GetSections(SectionOutletId outletId) =>
        _builder.GetSections(outletId);

    public CompositionModule Build() =>
        _builder.BuildModule(Id);
}
