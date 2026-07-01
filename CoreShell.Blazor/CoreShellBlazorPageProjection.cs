namespace CoreShell.Blazor;

public interface ICoreShellBlazorPageProjectionService
{
    Task<CoreShellBlazorResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default);
}

public sealed record CoreShellBlazorResolvedPage(
    CoreShellPageContribution Page,
    Type? ContentType,
    Type? LayoutType,
    IReadOnlyList<CoreShellBlazorSectionOutlet> SectionOutlets,
    IReadOnlyList<CoreShellBlazorSection> Sections)
{
    public IReadOnlyList<CoreShellBlazorSection> GetSections(CoreShellSectionOutletId outletId) =>
        Sections
            .Where(section => section.Section.OutletId == outletId)
            .ToArray();
}

public sealed record CoreShellBlazorSectionOutlet(
    CoreShellSectionOutletContribution Outlet,
    Type? LayoutType);

public sealed record CoreShellBlazorSection(
    CoreShellSectionContribution Section,
    Type ContentType,
    Type? LayoutType);

public sealed class CoreShellBlazorPageProjectionService(
    ICoreShellPageResolutionService pageResolutionService,
    ICoreShellContentResolver contentResolver,
    ICoreShellLayoutResolver? layoutResolver = null) : ICoreShellBlazorPageProjectionService
{
    public async Task<CoreShellBlazorResolvedPage?> ResolvePageAsync(
        CoreShellPageResolutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var resolved = await pageResolutionService.ResolvePageAsync(context, cancellationToken);
        if (resolved is null)
        {
            return null;
        }

        return new CoreShellBlazorResolvedPage(
            resolved.Page,
            ResolveContent(resolved.Page.Content),
            ResolveLayout(resolved.Page.Layout),
            resolved.SectionOutlets
                .Select(outlet => new CoreShellBlazorSectionOutlet(
                    outlet,
                    ResolveLayout(outlet.Layout)))
                .ToArray(),
            resolved.Sections
                .Select(section => new CoreShellBlazorSection(
                    section,
                    contentResolver.ResolveContentType(section.Content),
                    ResolveLayout(section.Layout)))
                .ToArray());
    }

    private Type? ResolveContent(CoreShellContentReference? content) =>
        content is { } reference
            ? contentResolver.ResolveContentType(reference)
            : null;

    private Type? ResolveLayout(CoreShellLayoutReference? layout)
    {
        if (layout is not { } reference)
        {
            return null;
        }

        if (layoutResolver is null)
        {
            throw new InvalidOperationException(
                $"CoreShell layout '{reference}' cannot be projected because no Blazor layout resolver is registered.");
        }

        return layoutResolver.ResolveLayoutType(reference);
    }
}
