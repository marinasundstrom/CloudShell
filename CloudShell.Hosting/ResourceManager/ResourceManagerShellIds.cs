using CloudShell.Hosting.Shell;
using CoreShell;

namespace CloudShell.Hosting.ResourceManager;

public static class ResourceManagerShellIds
{
    public static readonly CoreShellModuleId Module =
        CoreShellModuleId.Create("cloudshell.resource-manager");

    public static readonly CoreShellModuleId SettingsModule =
        CoreShellModuleId.Create("cloudshell.resource-manager.settings");

    public static readonly CoreShellPageId ResourcesPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources");

    public static readonly CoreShellPageId ResourceGraphPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.graph");

    public static readonly CoreShellPageId EnvironmentPage =
        CoreShellPageId.Create("cloudshell.resource-manager.environment");

    public static readonly CoreShellPageId ResourceDetailsPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.details");

    public static readonly CoreShellPageId HealthPage =
        CoreShellPageId.Create("cloudshell.resource-manager.health");

    public static readonly CoreShellPageId AddResourcePage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.add");

    public static readonly CoreShellPageId CreateResourceGroupPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.groups.new");

    public static readonly CoreShellPageId ResourceTemplatesPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.templates");

    public static readonly CoreShellPageId ResourceSettingsPage =
        CoreShellPageId.Create("cloudshell.resource-manager.resources.settings");

    public static readonly CoreShellMenuItemId ResourcesMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "resources");

    public static readonly CoreShellMenuItemId EnvironmentMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "environment");

    public static readonly CoreShellMenuItemId HealthMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "health");

    public static readonly CoreShellSectionId SettingsGeneralSection =
        CoreShellSectionId.Create(ShellIds.SettingsMainOutlet, "resource-manager");

    public static readonly CoreShellSectionId SettingsOrchestrationSection =
        CoreShellSectionId.Create(ShellIds.SettingsMainOutlet, "resource-manager-orchestration");

    public static readonly CoreShellSectionId SettingsSection = SettingsGeneralSection;
}
