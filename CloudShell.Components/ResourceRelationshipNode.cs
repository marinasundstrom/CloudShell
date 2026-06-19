namespace CloudShell.Components;

public sealed record ResourceRelationshipNode(
    string Label,
    string? Href = null,
    string? Summary = null,
    string? Metadata = null,
    bool IsMissing = false);
