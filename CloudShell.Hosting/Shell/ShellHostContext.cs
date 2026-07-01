using CoreShell;

namespace CloudShell.Hosting.Shell;

public sealed class ShellHostContext
{
    public ShellSettingsContext Settings { get; } = new();
}

public sealed class ShellSettingsContext
{
    public CoreShellSectionOutletExtensionPoint MainSections { get; } =
        new(ShellIds.SettingsPage, ShellIds.SettingsMainOutlet);
}
