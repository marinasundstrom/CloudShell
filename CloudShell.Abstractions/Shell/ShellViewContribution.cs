namespace CloudShell.Abstractions.Shell;

public sealed record ShellViewContribution(
    string Title,
    string Route,
    Type ComponentType,
    string Icon,
    int Order,
    string Group = "Workspace",
    bool ShowInNavigation = true);
