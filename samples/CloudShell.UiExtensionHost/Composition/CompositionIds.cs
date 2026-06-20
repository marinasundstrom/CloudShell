namespace CloudShell.UiExtensionHost.Composition;

public static class CompositionIds
{
    public static readonly MenuId MainMenu = new("menu.main");
    public static readonly MenuSectionId WorkspaceSection = new("menu-section.main.workspace");
    public static readonly MenuItemId WorkspaceItem = new("menu-item.main.workspace.sample");
    public static readonly MenuItemId ExtensionSectionItem = new("menu-item.main.workspace.extension-section");

    public static readonly PageId SampleWorkspacePage = new("page.sample-workspace");
    public static readonly SectionOutletId WorkspaceMainOutlet = new("section-outlet.sample-workspace.main");
    public static readonly SectionId ShellSurfaceSection = new("section.sample-workspace.shell-surface");
    public static readonly SectionId ExtensionContributionSection = new("section.sample-workspace.extension-contribution");
}

