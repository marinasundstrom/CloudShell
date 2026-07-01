using CoreShell;

namespace CloudShell.Hosting.Shell;

public static class ShellIds
{
    public static readonly CoreShellModuleId CoreModule = CoreShellModuleId.Create("cloudshell.core");
    public static readonly CoreShellModuleId NavigationModule = CoreShellModuleId.Create("cloudshell.navigation");
    public static readonly CoreShellMenuId MainMenu = CoreShellMenuId.Create("cloudshell.main");
    public static readonly CoreShellMenuGroupId WorkspaceMenuGroup = CoreShellMenuGroupId.Create(MainMenu, "workspace");
    public static readonly CoreShellMenuItemId OverviewMenuItem = CoreShellMenuItemId.Create(WorkspaceMenuGroup, "overview");
    public static readonly CoreShellPageId OverviewPage = CoreShellPageId.Create("cloudshell.overview");
    public static readonly CoreShellPageId SettingsPage = CoreShellPageId.Create("cloudshell.settings");
    public static readonly CoreShellSectionOutletId SettingsMainOutlet = CoreShellSectionOutletId.Create(SettingsPage, "main");
    public static readonly CoreShellSectionId SettingsOverviewSection = CoreShellSectionId.Create(SettingsMainOutlet, "overview");
    public static readonly CoreShellSectionId SettingsUsersSection = CoreShellSectionId.Create(SettingsMainOutlet, "users");
    public static readonly CoreShellSectionId SettingsExtensionsSection = CoreShellSectionId.Create(SettingsMainOutlet, "extensions");
}
