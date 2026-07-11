using CoreShell;

namespace CoreShell.FluentUiSample;

internal static class SampleShellIds
{
    public static readonly CoreShellModuleId HostModule = CoreShellModuleId.Create("sample.host");
    public static readonly CoreShellModuleId ExtensionModule = CoreShellModuleId.Create("sample.extension");

    public static readonly CoreShellMenuId MainMenu = CoreShellMenuId.Create("main");
    public static readonly CoreShellMenuGroupId WorkspaceMenuGroup = CoreShellMenuGroupId.Create(MainMenu, "workspace");
    public static readonly CoreShellMenuGroupId PlatformMenuGroup = CoreShellMenuGroupId.Create(MainMenu, "platform");

    public static readonly CoreShellPageId DashboardPage = CoreShellPageId.Create("dashboard");
    public static readonly CoreShellPageId OperationsPage = CoreShellPageId.Create("operations");
    public static readonly CoreShellPageId SettingsPage = CoreShellPageId.Create("settings");

    public static readonly CoreShellSectionOutletId DashboardMainOutlet =
        CoreShellSectionOutletId.Create(DashboardPage, "main");
    public static readonly CoreShellSectionOutletId OperationsMainOutlet =
        CoreShellSectionOutletId.Create(OperationsPage, "main");
    public static readonly CoreShellSectionOutletId SettingsMainOutlet =
        CoreShellSectionOutletId.Create(SettingsPage, "main");

    public static readonly CoreShellSectionId SystemOverviewSection =
        CoreShellSectionId.Create(DashboardMainOutlet, "overview");
    public static readonly CoreShellSectionId WorkQueueSection =
        CoreShellSectionId.Create(DashboardMainOutlet, "work-queue");
    public static readonly CoreShellSectionId ExtensionHealthSection =
        CoreShellSectionId.Create(DashboardMainOutlet, "extension-health");
    public static readonly CoreShellSectionId OperationsSummarySection =
        CoreShellSectionId.Create(OperationsMainOutlet, "summary");
    public static readonly CoreShellSectionId IncidentQueueSection =
        CoreShellSectionId.Create(OperationsMainOutlet, "incidents");
    public static readonly CoreShellSectionId GeneralSettingsSection =
        CoreShellSectionId.Create(SettingsMainOutlet, "general");
    public static readonly CoreShellSectionId AppearanceSettingsSection =
        CoreShellSectionId.Create(SettingsMainOutlet, "appearance");

    public static readonly CoreShellMenuItemId DashboardMenuItem =
        CoreShellMenuItemId.Create(WorkspaceMenuGroup, "dashboard");
    public static readonly CoreShellMenuItemId OperationsMenuItem =
        CoreShellMenuItemId.Create(WorkspaceMenuGroup, "operations");
    public static readonly CoreShellMenuItemId ExtensionHealthMenuItem =
        CoreShellMenuItemId.Create(WorkspaceMenuGroup, "extension-health");
    public static readonly CoreShellMenuItemId SettingsMenuItem =
        CoreShellMenuItemId.Create(PlatformMenuGroup, "settings");

    public static IReadOnlyDictionary<string, string> Icon(string icon) =>
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [CoreShellAttributeNames.Icon] = icon
        };
}
