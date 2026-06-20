using CloudShell.UI.Composition;

namespace CloudShell.CompositionSandbox;

public static class CompositionIds
{
    public static readonly MenuId MainMenu = new("menu.main");
    public static readonly MenuGroupId WorkspaceMenuGroup = new("menu-group.main.workspace");
    public static readonly MenuItemId WorkspaceItem = new("menu-item.main.workspace");
    public static readonly MenuItemId ReportsItem = new("menu-item.main.reports");
    public static readonly MenuItemId DashboardItem = new("menu-item.main.dashboard");
    public static readonly MenuItemId SettingsItem = new("menu-item.main.settings");
    public static readonly MenuItemId OverviewSectionItem = new("menu-item.main.workspace.overview");
    public static readonly MenuItemId ExtensionSectionItem = new("menu-item.main.workspace.extension");
    public static readonly PageId WorkspacePage = new("page.workspace");
    public static readonly PageId ReportsPage = new("page.reports");
    public static readonly PageId DashboardPage = new("page.dashboard");
    public static readonly PageId SettingsPage = new("page.settings");
    public static readonly SectionOutletId WorkspaceMainOutlet = new("section-outlet.workspace.main");
    public static readonly CompositionSectionOutletExtensionPoint WorkspaceMainSections = new(WorkspacePage, WorkspaceMainOutlet);
    public static readonly SectionOutletId ReportsMainOutlet = new("section-outlet.reports.main");
    public static readonly SectionOutletId DashboardMainOutlet = new("section-outlet.dashboard.main");
    public static readonly SectionOutletId SettingsMainOutlet = new("section-outlet.settings.main");
    public static readonly SectionId OverviewSection = new("section.workspace.main.overview");
    public static readonly SectionId ExtensionContributionSection = new("section.workspace.main.extension");
    public static readonly SectionId ReportsSummarySection = new("section.reports.main.summary");
    public static readonly SectionId DashboardStatusSection = new("section.dashboard.main.status");
    public static readonly SectionId DashboardActivitySection = new("section.dashboard.main.activity");
    public static readonly SectionId DashboardLayoutPatternSection = new("section.dashboard.main.layout-pattern");
    public static readonly SectionId SettingsGeneralSection = new("section.settings.main.general");
    public static readonly SectionId SettingsAdvancedSection = new("section.settings.main.advanced");
}
