namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceStandardViewSectionContribution(
    ResourceViewId ViewId,
    string Id,
    string Title,
    int Order,
    Type ComponentType);
