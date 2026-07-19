using CloudShell.Abstractions.ResourceManager;

namespace CloudShell.Hosting.ResourceManager;

internal enum ResourceTabSelectionIssueKind
{
    InvalidViewId,
    Unavailable
}

internal sealed record ResourceTabSelectionIssue(
    ResourceTabSelectionIssueKind Kind,
    string RequestedValue);

internal sealed record ResourceTabSelection(
    ResourceTabContribution? SelectedTab,
    ResourceViewId? RequestedViewId,
    ResourceTabSelectionIssue? Issue);

internal static class ResourceTabSelectionResolver
{
    public static ResourceTabSelection Resolve(
        IReadOnlyList<ResourceTabContribution> tabs,
        string? requestedRouteView,
        string? requestedQueryView,
        ResourceViewId? preferredViewId = null)
    {
        var hasRouteView = HasRequestedRouteViewSegment(requestedRouteView);
        var requestedValue = hasRouteView
            ? requestedRouteView
            : requestedQueryView;

        if (string.IsNullOrWhiteSpace(requestedValue))
        {
            return new ResourceTabSelection(
                SelectPreferredTab(tabs, preferredViewId),
                RequestedViewId: null,
                Issue: null);
        }

        var requestedDisplayValue = requestedValue.Trim();
        if (!TryResolveRequestedViewId(tabs, requestedDisplayValue, out var requestedViewId))
        {
            return new ResourceTabSelection(
                SelectedTab: null,
                RequestedViewId: null,
                new ResourceTabSelectionIssue(
                    hasRouteView
                        ? ResourceTabSelectionIssueKind.Unavailable
                        : ResourceTabSelectionIssueKind.InvalidViewId,
                    requestedDisplayValue));
        }

        if (!tabs.Any(tab => tab.Id == requestedViewId))
        {
            return new ResourceTabSelection(
                SelectedTab: null,
                requestedViewId,
                new ResourceTabSelectionIssue(
                    ResourceTabSelectionIssueKind.Unavailable,
                    requestedDisplayValue));
        }

        return new ResourceTabSelection(
            SelectPreferredTab(tabs, preferredViewId ?? requestedViewId),
            requestedViewId,
            Issue: null);
    }

    private static ResourceTabContribution? SelectPreferredTab(
        IReadOnlyList<ResourceTabContribution> tabs,
        ResourceViewId? preferredViewId) =>
        preferredViewId is { } viewId
            ? tabs.FirstOrDefault(tab => tab.Id == viewId)
            : tabs.FirstOrDefault();

    private static bool TryResolveRequestedViewId(
        IReadOnlyList<ResourceTabContribution> tabs,
        string requestedValue,
        out ResourceViewId requestedViewId)
    {
        var normalized = Uri.UnescapeDataString(requestedValue);
        if (ResourceViewId.TryParse(normalized, out requestedViewId))
        {
            return true;
        }

        var matchingTabs = tabs
            .Where(tab => string.Equals(
                ResourceManagerRoutes.GetResourceViewSegment(tab.Id),
                normalized,
                StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (matchingTabs.Length == 1)
        {
            requestedViewId = matchingTabs[0].Id;
            return true;
        }

        requestedViewId = default;
        return false;
    }

    private static bool HasRequestedRouteViewSegment(string? requestedRouteView) =>
        !string.IsNullOrWhiteSpace(requestedRouteView) &&
        !IsLegacyResourceDetailsSegment(requestedRouteView);

    private static bool IsLegacyResourceDetailsSegment(string value) =>
        string.Equals(value.Trim(), "details", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value.Trim(), "edit", StringComparison.OrdinalIgnoreCase);
}
