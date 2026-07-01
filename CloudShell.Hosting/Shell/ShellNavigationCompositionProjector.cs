using CloudShell.Abstractions.Shell;
using CoreShell.Composition;

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

                        if (TryGetCompositionTarget(item, out var target))
                        {
                            itemBuilder.Target(target);
                        }
                        else
                        {
                            itemBuilder.TargetHref(item.Href);
                        }
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

    private static bool TryGetCompositionTarget(
        NavItemContribution item,
        out CompositionTarget target)
    {
        var shellTarget = GetShellTarget(item.Target.ViewId, item.Href);
        if (shellTarget is not null)
        {
            target = shellTarget.Value;
            return true;
        }

        target = default;
        return false;
    }

    private static CompositionTarget? GetShellTarget(string? viewId, string href)
    {
        if (Matches(viewId, href, CoreShellViews.Overview, "/"))
        {
            return ShellCompositionIds.OverviewPage;
        }

        if (Matches(viewId, href, CoreShellViews.Settings, "/settings"))
        {
            return ShellCompositionIds.SettingsPage;
        }

        if (Matches(viewId, href, CoreShellViews.Users, "/account/users"))
        {
            return ShellCompositionIds.SettingsUsersSection;
        }

        if (Matches(viewId, href, CoreShellViews.Extensions, "/extensions"))
        {
            return ShellCompositionIds.SettingsExtensionsSection;
        }

        return null;
    }

    private static bool Matches(
        string? viewId,
        string href,
        string expectedViewId,
        string expectedHref) =>
        string.Equals(viewId, expectedViewId, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(NormalizeHref(href), NormalizeHref(expectedHref), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeHref(string href)
    {
        var normalized = href.Trim();
        return normalized.Length > 1 ? normalized.TrimEnd('/') : normalized;
    }

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
