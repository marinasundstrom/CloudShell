using CloudShell.UI.Composition;

namespace CloudShell.CompositionSandbox;

public static class CompositionIds
{
    public static readonly MenuId MainMenu = new("menu.main");
    public static readonly MenuSectionId WorkspaceMenuSection = new("menu-section.main.workspace");
    public static readonly MenuItemId WorkspaceItem = new("menu-item.main.workspace");
    public static readonly MenuItemId ReportsItem = new("menu-item.main.reports");
    public static readonly MenuItemId DashboardItem = new("menu-item.main.dashboard");
    public static readonly MenuItemId OverviewSectionItem = new("menu-item.main.workspace.overview");
    public static readonly MenuItemId ExtensionSectionItem = new("menu-item.main.workspace.extension");
    public static readonly PageId WorkspacePage = new("page.workspace");
    public static readonly PageId ReportsPage = new("page.reports");
    public static readonly PageId DashboardPage = new("page.dashboard");
    public static readonly SectionOutletId WorkspaceMainOutlet = new("section-outlet.workspace.main");
    public static readonly SectionOutletId ReportsMainOutlet = new("section-outlet.reports.main");
    public static readonly SectionOutletId DashboardMainOutlet = new("section-outlet.dashboard.main");
    public static readonly SectionId OverviewSection = new("section.workspace.main.overview");
    public static readonly SectionId ExtensionContributionSection = new("section.workspace.main.extension");
    public static readonly SectionId ReportsSummarySection = new("section.reports.main.summary");
    public static readonly SectionId DashboardStatusSection = new("section.dashboard.main.status");
    public static readonly SectionId DashboardActivitySection = new("section.dashboard.main.activity");
    public static readonly SectionId DashboardLayoutPatternSection = new("section.dashboard.main.layout-pattern");
}
