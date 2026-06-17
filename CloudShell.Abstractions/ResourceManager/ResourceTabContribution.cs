namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTabContribution(
    ResourceViewId Id,
    string Title,
    int Order,
    Type ComponentType,
    bool ShowsApplyButton = false,
    string? GroupTitle = null,
    string? Icon = null)
{
    public string GroupId => Id.GroupId;

    public string ViewId => Id.Identifier;
}
