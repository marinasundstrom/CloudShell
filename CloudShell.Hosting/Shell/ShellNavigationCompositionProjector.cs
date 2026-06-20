using CloudShell.Abstractions.Shell;
using CloudShell.UI.Composition;

namespace CloudShell.Hosting.Shell;

public sealed class ShellNavigationCompositionProjector(ShellCatalog shellCatalog)
{
    public CompositionModule CreateModule() =>
        CompositionModule.Create(
            ShellCompositionIds.NavigationModule,
            module =>
            {
                var navigationItems = shellCatalog.NavigationItems.ToArray();
                var menu = module.AddMenu(ShellCompositionIds.MainMenu, "Main");
                var itemIds = navigationItems.ToDictionary(
                    item => item.Id,
                    item => CreateItemId(item),
                    StringComparer.OrdinalIgnoreCase);

                foreach (var groupItems in navigationItems.GroupBy(NormalizeGroupTitle))
                {
                    var group = menu.AddGroup(
                        MenuGroupId.Create(ShellCompositionIds.MainMenu, CreateIdentifier(groupItems.Key)),
                        groupItems.Key,
                        groupItems.Min(item => item.Order));

                    foreach (var item in groupItems)
                    {
                        var itemBuilder = group
                            .AddItem(itemIds[item.Id], item.Text, item.Order)
                            .WithAttribute(CompositionAttributeNames.Icon, item.Icon)
                            .RequiresPermissions(item.PermissionsRequiredForNavigation);

                        if (!string.IsNullOrWhiteSpace(item.ParentId) &&
                            itemIds.TryGetValue(item.ParentId, out var parentId))
                        {
                            itemBuilder.WithParent(parentId);
                        }

                        itemBuilder.TargetHref(item.Href);
                    }
                }
            });

    private static MenuItemId CreateItemId(NavItemContribution item)
    {
        var groupId = MenuGroupId.Create(
            ShellCompositionIds.MainMenu,
            CreateIdentifier(NormalizeGroupTitle(item)));

        return MenuItemId.Create(groupId, CreateIdentifier(item.Id));
    }

    private static string NormalizeGroupTitle(NavItemContribution item) =>
        NormalizeGroupTitle(item.Group);

    private static string NormalizeGroupTitle(string? group) =>
        string.IsNullOrWhiteSpace(group) ? "Workspace" : group.Trim();

    private static string CreateIdentifier(string value)
    {
        var normalized = new string(value
            .Trim()
            .Select(character => char.IsLetterOrDigit(character) ? char.ToLowerInvariant(character) : '.')
            .ToArray());

        normalized = string.Join(
            '.',
            normalized
                .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));

        return string.IsNullOrWhiteSpace(normalized) ? "item" : normalized;
    }
}
