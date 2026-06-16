namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceStandardViewSectionContribution(
    string ViewId,
    string Id,
    string Title,
    int Order,
    Type ComponentType);
