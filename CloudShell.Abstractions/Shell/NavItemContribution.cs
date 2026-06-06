namespace CloudShell.Abstractions.Shell;

public sealed record NavItemContribution(
    string Text,
    string Href,
    string Icon,
    int Order,
    string Group = "Workspace");
