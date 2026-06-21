using CloudShell.UI.Composition;

namespace CloudShell.Hosting.Shell;

public static class ShellCompositionIds
{
    public static readonly CompositionModuleId CoreModule = CompositionModuleId.Create("cloudshell.core");
    public static readonly CompositionModuleId NavigationModule = CompositionModuleId.Create("cloudshell.navigation");
    public static readonly MenuId MainMenu = MenuId.Create("cloudshell.main");
    public static readonly MenuGroupId WorkspaceMenuGroup = MenuGroupId.Create(MainMenu, "workspace");
    public static readonly MenuItemId OverviewMenuItem = MenuItemId.Create(WorkspaceMenuGroup, "overview");
    public static readonly PageId OverviewPage = PageId.Create("cloudshell.overview");
    public static readonly PageId SettingsPage = PageId.Create("cloudshell.settings");
    public static readonly SectionOutletId SettingsMainOutlet = SectionOutletId.Create(SettingsPage, "main");
    public static readonly SectionId SettingsOverviewSection = SectionId.Create(SettingsMainOutlet, "overview");
    public static readonly SectionId SettingsUsersSection = SectionId.Create(SettingsMainOutlet, "users");
    public static readonly SectionId SettingsExtensionsSection = SectionId.Create(SettingsMainOutlet, "extensions");
}
