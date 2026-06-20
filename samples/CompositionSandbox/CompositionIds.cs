using CloudShell.UI.Composition;

namespace CloudShell.CompositionSandbox;

public static class CompositionIds
{
    public static readonly MenuId MainMenu = new("menu.main");
    public static readonly MenuSectionId WorkspaceMenuSection = new("menu-section.main.workspace");
    public static readonly MenuItemId WorkspaceItem = new("menu-item.main.workspace");
    public static readonly MenuItemId OverviewSectionItem = new("menu-item.main.workspace.overview");
    public static readonly MenuItemId ExtensionSectionItem = new("menu-item.main.workspace.extension");
    public static readonly PageId WorkspacePage = new("page.workspace");
    public static readonly SectionOutletId WorkspaceMainOutlet = new("section-outlet.workspace.main");
    public static readonly SectionId OverviewSection = new("section.workspace.main.overview");
    public static readonly SectionId ExtensionContributionSection = new("section.workspace.main.extension");
}
