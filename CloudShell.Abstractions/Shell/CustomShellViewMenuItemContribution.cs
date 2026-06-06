namespace CloudShell.Abstractions.Shell;

public sealed record CustomShellViewMenuItemContribution(
    string ViewId,
    string Id,
    string Title,
    Type ComponentType,
    int Order,
    string? Description = null);
