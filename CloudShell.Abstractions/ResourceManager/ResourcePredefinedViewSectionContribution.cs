namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourcePredefinedViewSectionContribution(
    ResourceViewId ViewId,
    string Id,
    string Title,
    int Order,
    Type ComponentType);
