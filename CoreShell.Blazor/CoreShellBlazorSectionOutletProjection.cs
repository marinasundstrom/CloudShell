namespace CoreShell.Blazor;

public interface ICoreShellBlazorSectionOutletProjectionService
{
    Task<CoreShellBlazorResolvedSectionOutlet?> ResolveSectionOutletAsync(
        CoreShellPageId pageId,
        CoreShellSectionOutletId outletId,
        CancellationToken cancellationToken = default);
}

public sealed record CoreShellBlazorResolvedSectionOutlet(
    CoreShellPageContribution Page,
    CoreShellBlazorSectionOutlet Outlet,
    IReadOnlyList<CoreShellBlazorSection> Sections);

public sealed class CoreShellBlazorSectionOutletProjectionService(
    ICoreShellPageService pageService,
    ICoreShellSectionService sectionService,
    ICoreShellContentResolver contentResolver,
    ICoreShellLayoutResolver? layoutResolver = null) : ICoreShellBlazorSectionOutletProjectionService
{
    public async Task<CoreShellBlazorResolvedSectionOutlet?> ResolveSectionOutletAsync(
        CoreShellPageId pageId,
        CoreShellSectionOutletId outletId,
        CancellationToken cancellationToken = default)
    {
        var page = await pageService.GetPageAsync(pageId, cancellationToken);
        if (page is null)
        {
            return null;
        }

        var outlet = (await sectionService.GetSectionOutletsAsync(pageId, cancellationToken))
            .FirstOrDefault(outlet => outlet.Id == outletId);
        if (outlet is null)
        {
            return null;
        }

        var sections = await sectionService.GetSectionsAsync(outletId, cancellationToken);
        return new CoreShellBlazorResolvedSectionOutlet(
            page,
            new CoreShellBlazorSectionOutlet(outlet, ResolveLayout(outlet.Layout)),
            sections
                .Select(section => new CoreShellBlazorSection(
                    section,
                    contentResolver.ResolveContentType(section.Content),
                    ResolveLayout(section.Layout)))
                .ToArray());
    }

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
