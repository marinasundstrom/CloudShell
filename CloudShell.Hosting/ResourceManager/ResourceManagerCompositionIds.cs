using CloudShell.Hosting.Shell;
using CloudShell.UI.Composition;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceManagerCompositionIds
{
    public static readonly CompositionModuleId SettingsModule =
        CompositionModuleId.Create("cloudshell.resource-manager.settings");

    public static readonly SectionId SettingsSection =
        SectionId.Create(ShellCompositionIds.SettingsMainOutlet, "resource-manager");
}
