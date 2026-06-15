namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTabContribution(
    string Id,
    string Title,
    int Order,
    Type ComponentType,
    bool ShowsApplyButton = false,
    string? GroupTitle = null);
