namespace CloudShell.Abstractions.Shell;

public sealed record CustomShellViewContribution(
    string Id,
    string Title,
    string Route,
    string Icon,
    int Order,
    string Group = "Workspace",
    string? Description = null,
    bool ShowInNavigation = true,
    IReadOnlyList<CustomShellViewMenuItemContribution>? MenuItems = null)
{
    public IReadOnlyList<CustomShellViewMenuItemContribution> ViewMenuItems => MenuItems ?? [];
}
