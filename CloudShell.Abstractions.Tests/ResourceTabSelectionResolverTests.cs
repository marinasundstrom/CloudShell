using CloudShell.Abstractions.ResourceManager;
using CloudShell.Hosting.ResourceManager;

namespace CloudShell.Abstractions.Tests;

public sealed class ResourceTabSelectionResolverTests
{
    private static readonly ResourceTabContribution OverviewTab = new(
        ResourcePredefinedViewIds.Overview,
        "Overview",
        0,
        typeof(object));

    private static readonly ResourceTabContribution LogsTab = new(
        ResourcePredefinedViewIds.Logs,
        "Logs",
        10,
        typeof(object));

    private static readonly IReadOnlyList<ResourceTabContribution> Tabs =
        [OverviewTab, LogsTab];

    [Fact]
    public void Resolve_SelectsRouteSegmentView()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: "LOGS",
            requestedQueryView: null);

        Assert.Same(LogsTab, selection.SelectedTab);
        Assert.Equal(ResourcePredefinedViewIds.Logs, selection.RequestedViewId);
        Assert.Null(selection.Issue);
    }

    [Fact]
    public void Resolve_SelectsCanonicalEncodedQueryViewOnLegacyRoute()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: "details",
            requestedQueryView: "telemetry%3Alogs");

        Assert.Same(LogsTab, selection.SelectedTab);
        Assert.Equal(ResourcePredefinedViewIds.Logs, selection.RequestedViewId);
        Assert.Null(selection.Issue);
    }

    [Fact]
    public void Resolve_ReportsInvalidQueryViewId()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: null,
            requestedQueryView: "not-a-view-id");

        Assert.Null(selection.SelectedTab);
        Assert.Null(selection.RequestedViewId);
        Assert.Equal(ResourceTabSelectionIssueKind.InvalidViewId, selection.Issue?.Kind);
        Assert.Equal("not-a-view-id", selection.Issue?.RequestedValue);
    }

    [Fact]
    public void Resolve_ReportsUnavailableCanonicalView()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: null,
            requestedQueryView: ResourcePredefinedViewIds.Metrics.Value);

        Assert.Null(selection.SelectedTab);
        Assert.Equal(ResourcePredefinedViewIds.Metrics, selection.RequestedViewId);
        Assert.Equal(ResourceTabSelectionIssueKind.Unavailable, selection.Issue?.Kind);
    }

    [Fact]
    public void Resolve_ReportsAmbiguousRouteSegmentAsUnavailable()
    {
        var duplicateLogs = new ResourceTabContribution(
            new ResourceViewId(ResourceTabGroupIds.Management, "logs"),
            "Management logs",
            20,
            typeof(object));

        var selection = ResourceTabSelectionResolver.Resolve(
            [.. Tabs, duplicateLogs],
            requestedRouteView: "logs",
            requestedQueryView: null);

        Assert.Null(selection.SelectedTab);
        Assert.Null(selection.RequestedViewId);
        Assert.Equal(ResourceTabSelectionIssueKind.Unavailable, selection.Issue?.Kind);
    }

    [Fact]
    public void Resolve_UsesPreferredViewDuringInteractiveNavigation()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: "overview",
            requestedQueryView: null,
            preferredViewId: ResourcePredefinedViewIds.Logs);

        Assert.Same(LogsTab, selection.SelectedTab);
        Assert.Equal(ResourcePredefinedViewIds.Overview, selection.RequestedViewId);
        Assert.Null(selection.Issue);
    }

    [Fact]
    public void Resolve_SelectsFirstViewWithoutExplicitSelection()
    {
        var selection = ResourceTabSelectionResolver.Resolve(
            Tabs,
            requestedRouteView: null,
            requestedQueryView: null);

        Assert.Same(OverviewTab, selection.SelectedTab);
        Assert.Null(selection.RequestedViewId);
        Assert.Null(selection.Issue);
    }
}
