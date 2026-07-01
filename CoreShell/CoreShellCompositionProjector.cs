using CoreShell.Composition;

namespace CoreShell;

internal sealed class CoreShellCompositionProjector(
    ICoreShellContentResolver contentResolver)
{
    public CompositionModule CreateModule(CoreShellModule module)
    {
        ArgumentNullException.ThrowIfNull(module);

        return CompositionModule.Create(ToCompositionId(module.Id), builder =>
        {
            foreach (var page in module.Pages)
            {
                builder.AddPage(
                    ToCompositionId(page.Id),
                    page.Title,
                    page.Route,
                    page.IsExtendable,
                    ToCompositionAuthorization(page.Authorization));
            }

            foreach (var outlet in module.SectionOutlets)
            {
                builder
                    .Extend(ToCompositionId(outlet.PageId))
                    .AddSections(
                        ToCompositionId(outlet.Id),
                        outlet.IsExtendable,
                        ToCompositionAuthorization(outlet.Authorization),
                        ToCompositionAddressMode(outlet.AddressMode),
                        outlet.SelectionKey);
            }

            foreach (var section in module.Sections)
            {
                builder
                    .GetSections(ToCompositionId(section.PageId), ToCompositionId(section.OutletId))
                    .AddSection(
                        ToCompositionId(section.Id),
                        section.Title,
                        contentResolver.ResolveContentType(section.Content),
                        section.Order,
                        ToCompositionAuthorization(section.Authorization),
                        section.Attributes);
            }

            foreach (var menu in module.Menus)
            {
                var menuBuilder = builder.AddMenu(
                    ToCompositionId(menu.Id),
                    menu.Title,
                    ToCompositionAuthorization(menu.Authorization));

                foreach (var item in menu.Items)
                {
                    ProjectMenuItem(menuBuilder.AddItem(ToCompositionId(item.Id), item.Title, item.Order), item);
                }

                foreach (var group in menu.Groups)
                {
                    var groupBuilder = menuBuilder.AddGroup(
                        ToCompositionId(group.Id),
                        group.Title,
                        group.Order,
                        ToCompositionAuthorization(group.Authorization));

                    foreach (var item in group.Items)
                    {
                        ProjectMenuItem(groupBuilder.AddItem(ToCompositionId(item.Id), item.Title, item.Order), item);
                    }
                }
            }
        });
    }

    private static void ProjectMenuItem(
        CompositionMenuItemBuilder builder,
        CoreShellMenuItemContribution item)
    {
        if (item.ParentId is { } parentId)
        {
            builder.WithParent(ToCompositionId(parentId));
        }

        builder
            .WithAttributes(item.Attributes)
            .RequiresAuthorization(ToCompositionAuthorization(item.Authorization))
            .Target(ToCompositionTarget(item.Target));
    }

    private static CompositionModuleId ToCompositionId(CoreShellModuleId id) =>
        CompositionModuleId.Create(id.Value);

    private static MenuId ToCompositionId(CoreShellMenuId id) =>
        MenuId.Create(id.Value);

    private static MenuGroupId ToCompositionId(CoreShellMenuGroupId id) =>
        new($"menu-group.{id.Value}");

    private static MenuItemId ToCompositionId(CoreShellMenuItemId id) =>
        new($"menu-item.{id.Value}");

    private static PageId ToCompositionId(CoreShellPageId id) =>
        PageId.Create(id.Value);

    private static SectionOutletId ToCompositionId(CoreShellSectionOutletId id) =>
        new($"section-outlet.{id.Value}");

    private static SectionId ToCompositionId(CoreShellSectionId id) =>
        new($"section.{id.Value}");

    private static CompositionTarget ToCompositionTarget(CoreShellTarget target) =>
        target.Kind switch
        {
            CoreShellTargetKind.Page => CompositionTarget.ForPage(ToCompositionId(new CoreShellPageId(target.Value))),
            CoreShellTargetKind.Section => CompositionTarget.ForSection(ToCompositionId(new CoreShellSectionId(target.Value))),
            CoreShellTargetKind.Href => CompositionTarget.ForHref(target.Value),
            _ => throw new InvalidOperationException($"Unsupported CoreShell target kind '{target.Kind}'.")
        };

    private static CompositionSectionAddressMode ToCompositionAddressMode(
        CoreShellSectionAddressMode addressMode) =>
        addressMode switch
        {
            CoreShellSectionAddressMode.Parent => CompositionSectionAddressMode.Parent,
            CoreShellSectionAddressMode.Child => CompositionSectionAddressMode.Child,
            _ => throw new InvalidOperationException($"Unsupported CoreShell section address mode '{addressMode}'.")
        };

    private static CompositionAuthorizationRequirements ToCompositionAuthorization(
        CoreShellAuthorizationRequirements authorization) =>
        new(
            authorization.AnyPermissions,
            authorization.Policies,
            authorization.Roles,
            authorization.Claims
                .Select(claim => new CompositionClaimRequirement(claim.Type, claim.Value))
                .ToArray());
}
