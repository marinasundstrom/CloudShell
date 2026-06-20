using CloudShell.UI.Composition;

namespace CloudShell.Hosting.Shell;

public static class ShellCompositionIds
{
    public static readonly CompositionModuleId CoreModule = CompositionModuleId.Create("cloudshell.core");
    public static readonly PageId SettingsPage = PageId.Create("cloudshell.settings");
    public static readonly SectionOutletId SettingsMainOutlet = SectionOutletId.Create(SettingsPage, "main");
    public static readonly SectionId SettingsOverviewSection = SectionId.Create(SettingsMainOutlet, "overview");
    public static readonly SectionId SettingsPlatformSection = SectionId.Create(SettingsMainOutlet, "platform");
}
