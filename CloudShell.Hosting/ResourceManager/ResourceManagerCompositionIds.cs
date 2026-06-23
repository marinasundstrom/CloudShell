using CloudShell.Hosting.Shell;
using CloudShell.UI.Composition;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceManagerCompositionIds
{
    public static readonly CompositionModuleId Module =
        CompositionModuleId.Create("cloudshell.resource-manager");

    public static readonly CompositionModuleId SettingsModule =
        CompositionModuleId.Create("cloudshell.resource-manager.settings");

    public static readonly PageId ResourcesPage =
        PageId.Create("cloudshell.resource-manager.resources");

    public static readonly PageId ResourceGraphPage =
        PageId.Create("cloudshell.resource-manager.resources.graph");

    public static readonly PageId EnvironmentPage =
        PageId.Create("cloudshell.resource-manager.environment");

    public static readonly PageId ResourceDetailsPage =
        PageId.Create("cloudshell.resource-manager.resources.details");

    public static readonly PageId HealthPage =
        PageId.Create("cloudshell.resource-manager.health");

    public static readonly PageId AddResourcePage =
        PageId.Create("cloudshell.resource-manager.resources.add");

    public static readonly PageId CreateResourceGroupPage =
        PageId.Create("cloudshell.resource-manager.resources.groups.new");

    public static readonly PageId ResourceTemplatesPage =
        PageId.Create("cloudshell.resource-manager.resources.templates");

    public static readonly PageId ResourceSettingsPage =
        PageId.Create("cloudshell.resource-manager.resources.settings");

    public static readonly MenuItemId ResourcesMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "resources");

    public static readonly MenuItemId EnvironmentMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "environment");

    public static readonly MenuItemId HealthMenuItem =
        MenuItemId.Create(ShellCompositionIds.WorkspaceMenuGroup, "health");

    public static readonly SectionId SettingsGeneralSection =
        SectionId.Create(ShellCompositionIds.SettingsMainOutlet, "resource-manager");

    public static readonly SectionId SettingsOrchestrationSection =
        SectionId.Create(ShellCompositionIds.SettingsMainOutlet, "resource-manager-orchestration");

    public static readonly SectionId SettingsSection = SettingsGeneralSection;
}
