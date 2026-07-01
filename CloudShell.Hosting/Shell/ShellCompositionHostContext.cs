using CoreShell.Composition;

namespace CloudShell.Hosting.Shell;

public sealed class ShellCompositionHostContext
    : ICompositionHostContext
{
    public ShellSettingsCompositionContext Settings { get; } = new();
}

public sealed class ShellSettingsCompositionContext
{
    public CompositionSectionOutletExtensionPoint MainSections { get; } =
        new(ShellCompositionIds.SettingsPage, ShellCompositionIds.SettingsMainOutlet);
}
