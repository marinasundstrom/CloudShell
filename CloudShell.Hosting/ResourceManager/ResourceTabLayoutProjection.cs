using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.Components.Layout;
using CoreShell;

namespace CloudShell.Hosting.ResourceManager;

internal static class ResourceTabLayoutProjection
{
    public static IReadOnlyList<CloudShellTabbedLayoutItem> CreateItems(
        IReadOnlyList<ResourceTabContribution> tabs,
        ICoreShellRouteService? routes = null,
        string? resourceId = null) =>
        BuildGroups(tabs)
            .SelectMany((group, groupIndex) => group.Tabs
                .OrderBy(GetTabOrder)
                .ThenBy(tab => tab.Title, StringComparer.OrdinalIgnoreCase)
                .Select((tab, tabIndex) =>
                new CloudShellTabbedLayoutItem(
                    tab.Id.Value,
                    tab.Title,
                    (groupIndex * 10_000) + tabIndex,
                    Group: group.Id,
                    GroupTitle: group.Title,
                    Icon: tab.Icon,
                    Href: BuildHref(routes, resourceId, tab.Id))))
            .ToArray();

    private static string? BuildHref(
        ICoreShellRouteService? routes,
        string? resourceId,
        ResourceViewId tabId) =>
        routes is null || string.IsNullOrWhiteSpace(resourceId)
            ? null
            : ResourceManagerShellLinks.ResourceDetails(routes, resourceId, tabId);

    private static IReadOnlyList<ResourceTabGroup> BuildGroups(
        IReadOnlyList<ResourceTabContribution> tabs)
    {
        var groups = new List<ResourceTabGroup>();
        foreach (var tab in tabs)
        {
            var groupId = tab.GroupId;
            var groupTitle = ResolveGroupTitle(tab);
            var currentGroup = groups.FirstOrDefault(group =>
                string.Equals(group.Id, groupId, StringComparison.OrdinalIgnoreCase));
            if (currentGroup is null)
            {
                currentGroup = new ResourceTabGroup(groupId, groupTitle, []);
                groups.Add(currentGroup);
            }

            currentGroup.Tabs.Add(tab);
        }

        return groups
            .Select((group, index) => new { Group = group, Index = index })
            .OrderBy(item => GetGroupOrder(item.Group.Id))
            .ThenBy(item => item.Index)
            .Select(item => item.Group)
            .ToArray();
    }

    private static int GetGroupOrder(string groupId) =>
        groupId switch
        {
            ResourceTabGroupIds.General => 0,
            ResourceTabGroupIds.Application => 10,
            ResourceTabGroupIds.Messaging => 20,
            ResourceTabGroupIds.Networking => 30,
            ResourceTabGroupIds.Runtime => 40,
            ResourceTabGroupIds.Storage => 50,
            ResourceTabGroupIds.Environment => 60,
            ResourceTabGroupIds.Telemetry => 90,
            ResourceTabGroupIds.Management => 100,
            _ => 80
        };

    private static int GetTabOrder(ResourceTabContribution tab) =>
        tab.Id == ResourcePredefinedViewIds.Overview
            ? int.MinValue
            : tab.Order;

    private static string ResolveGroupTitle(ResourceTabContribution tab) =>
        !string.IsNullOrWhiteSpace(tab.GroupTitle)
            ? tab.GroupTitle.Trim()
            : tab.GroupId switch
            {
                ResourceTabGroupIds.General => ResourceTabGroupTitles.General,
                ResourceTabGroupIds.Application => ResourceTabGroupTitles.Application,
                ResourceTabGroupIds.Messaging => ResourceTabGroupTitles.Messaging,
                ResourceTabGroupIds.Networking => ResourceTabGroupTitles.Networking,
                ResourceTabGroupIds.Storage => ResourceTabGroupTitles.Storage,
                ResourceTabGroupIds.Environment => ResourceTabGroupTitles.Environment,
                ResourceTabGroupIds.Telemetry => ResourceTabGroupTitles.Telemetry,
                ResourceTabGroupIds.Management => ResourceTabGroupTitles.Management,
                ResourceTabGroupIds.Runtime => ResourceTabGroupTitles.Runtime,
                _ => tab.GroupId
            };

    private sealed record ResourceTabGroup(
        string Id,
        string Title,
        List<ResourceTabContribution> Tabs);
}
