namespace CloudShell.Abstractions.ResourceManager;

public sealed record ResourceTabContribution(
    ResourceViewId Id,
    string Title,
    int Order,
    Type ComponentType,
    bool ShowsApplyButton = false,
    string? GroupTitle = null,
    string? Icon = null,
    bool IsOperational = false)
{
    /// <summary>The component supplies its own heading and panel layout.</summary>
    public bool OwnsLayout { get; init; }

    public string GroupId => Id.GroupId;

    public string ViewId => Id.Identifier;
}
