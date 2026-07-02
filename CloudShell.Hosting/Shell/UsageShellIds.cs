using CoreShell;

namespace CloudShell.Hosting.Shell;

public static class UsageShellIds
{
    public static readonly CoreShellModuleId Module =
        CoreShellModuleId.Create("cloudshell.usage");

    public static readonly CoreShellPageId UsagePage =
        CoreShellPageId.Create("cloudshell.usage");

    public static readonly CoreShellMenuItemId UsageMenuItem =
        CoreShellMenuItemId.Create(ShellIds.WorkspaceMenuGroup, "usage");
}
